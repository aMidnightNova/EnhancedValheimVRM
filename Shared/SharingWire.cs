using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;

namespace EnhancedValheimVRM.Sharing
{
    // This protocol runs exclusively over the TCP server's TCP sockets, never ZPackage/RPC.
    public static class SharingWire
    {
        // the names on the wire, never change these three. a build that cannot read them cannot tell the
        // player why, and the version handshake is the one thing that can say the versions differ
        public const int Magic = 0x45565631; // EVV1, the mod prefix and the wire version
        public const string ControlRpc = "EVV_SharingControl1";
        public const string VersionRpc = "EVV_Version";

        public const int MaxBundleBytes = 1024 * 1024 * 1024;
        public const int DefaultBundleLimitBytes = 384 * 1024 * 1024;
        public const int MaxExpandedVrmBytes = 1024 * 1024 * 1024 - 1048576;
        public const int MaxSettingsBytes = 128 * 1024;

        // ping is the plain "this port reaches the sharing server" check a client runs before using an address
        public const byte Ping = 1, Upload = 2, Download = 3;

        // Two blobs per character, both named by the model's hash: <hash>.vrm.bundle holds the
        // model and <hash>.settings.bundle holds settings + outfits and is replaced in place.
        public const string AvatarKind = "vrm", ProfileKind = "settings";

        public static void ValidateKind(string kind)
        {
            if (kind != AvatarKind && kind != ProfileKind) throw new InvalidDataException("Invalid blob kind.");
        }

        // old versions of a character used to pile up forever on the server and in every clients cache.
        // keep only the blobs named after the current model hash. a file that is still open somewhere
        // (a download in flight) is skipped and caught on the next pass.
        public static void PruneBlobs(string directory, string keepHash)
        {
            if (string.IsNullOrEmpty(keepHash) || !Directory.Exists(directory)) return;
            foreach (var file in Directory.GetFiles(directory, "*.bundle"))
            {
                if (Path.GetFileName(file).StartsWith(keepHash + ".", StringComparison.Ordinal)) continue;
                try
                {
                    File.Delete(file);
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }

        public static string BlobFileName(string kind, string hash)
        {
            ValidateKind(kind);
            ValidateHash(hash);
            return hash + "." + kind + ".bundle";
        }

        public static byte[] ReadBytes(BinaryReader reader, int maximum)
        {
            var length = reader.ReadInt32();
            if (length < 0 || length > maximum) throw new InvalidDataException("Invalid frame size.");
            var bytes = reader.ReadBytes(length);
            if (bytes.Length != length) throw new EndOfStreamException();
            return bytes;
        }

        public static void WriteBytes(BinaryWriter writer, byte[] bytes)
        {
            writer.Write(bytes.Length);
            writer.Write(bytes);
        }

        public static string ReadText(BinaryReader reader, int maximum = 256)
        {
            return new UTF8Encoding(false, true).GetString(ReadBytes(reader, maximum));
        }

        public static void WriteText(BinaryWriter writer, string text)
        {
            WriteBytes(writer, Encoding.UTF8.GetBytes(text));
        }

        // comment and blank lines are for the owner, nobody else needs to download them
        public static string StripComments(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            var kept = new List<string>();
            foreach (var raw in text.Split('\n'))
            {
                var line = raw.TrimEnd('\r').Trim();
                if (line.Length == 0 || line.StartsWith("#") || line.StartsWith("//") || line.StartsWith(";")) continue;
                kept.Add(line);
            }

            return kept.Count == 0 ? "" : string.Join("\n", kept) + "\n";
        }

        public static void ValidateOutfitName(string name)
        {
            if (string.IsNullOrWhiteSpace(name) || name.Length > 80 || Encoding.UTF8.GetByteCount(name) > 256 ||
                name.IndexOfAny(new[] { '[', ']', '\r', '\n' }) >= 0)
                throw new InvalidDataException("Invalid outfit name.");
            foreach (var character in name)
            {
                if (char.IsControl(character)) throw new InvalidDataException("Invalid outfit name.");
            }
        }

        public static bool IsValidOverride(string name, bool blend, float value)
        {
            if (string.IsNullOrWhiteSpace(name) || name.Length > 128 || Encoding.UTF8.GetByteCount(name) > 256 ||
                float.IsNaN(value) || float.IsInfinity(value))
                return false;
            foreach (var c in name)
            {
                if (char.IsControl(c)) return false;
            }

            return blend ? value >= 0 && value <= 100 : value == 0 || value == 1;
        }

        public static void ValidateId(long id)
        {
            if (id == 0) throw new InvalidDataException("Character ID is not initialized.");
        }

        public static void ValidateHash(string hash)
        {
            if (hash == null || hash.Length != 64) throw new InvalidDataException("Invalid hash.");
            foreach (var c in hash)
            {
                if (!(c >= '0' && c <= '9') && !(c >= 'a' && c <= 'f')) throw new InvalidDataException("Invalid hash.");
            }
        }

        public static void WriteThrottled(BinaryWriter writer,
            byte[] bytes,
            CancellationToken cancellation,
            int bytesPerSecond)
        {
            writer.Write(bytes.Length);
            writer.Flush();
            using (var input = new MemoryStream(bytes, false))
                CopyThrottled(input, writer.BaseStream, new BandwidthLimiter(bytesPerSecond), cancellation);
        }

        public static void CopyThrottled(Stream input,
            Stream output,
            BandwidthLimiter limiter,
            CancellationToken cancellation)
        {
            var buffer = new byte[16384];
            while (true)
            {
                cancellation.ThrowIfCancellationRequested();
                var count = input.Read(buffer, 0, buffer.Length);
                if (count == 0) break;
                limiter.WaitForBytes(count, cancellation);
                output.Write(buffer, 0, count);
                output.Flush();
            }
        }
    }

    // Version/Hash describe the avatar blob (the VRM); ProfileVersion/ProfileHash describe the
    // settings blob (settings + outfits). A settings edit changes only the second pair.
    public sealed class BundleInfo
    {
        public string Version = "", Hash = "";
        public string ProfileVersion = "", ProfileHash = "";

        public bool HasAvatar => !string.IsNullOrEmpty(Version) && !string.IsNullOrEmpty(Hash);
        public bool HasProfile => !string.IsNullOrEmpty(ProfileVersion) && !string.IsNullOrEmpty(ProfileHash);
        public bool IsComplete => HasAvatar && HasProfile;
        public string Key => Hash + ":" + ProfileHash;

        // Integrity hash and version of one blob; both files are named by the model hash.
        public string HashOf(string kind)
        {
            return kind == SharingWire.AvatarKind ? Hash : ProfileHash;
        }

        public string VersionOf(string kind)
        {
            return kind == SharingWire.AvatarKind ? Version : ProfileVersion;
        }

        public bool SameAs(BundleInfo other)
        {
            return other != null && other.Version == Version && other.Hash == Hash &&
                other.ProfileVersion == ProfileVersion && other.ProfileHash == ProfileHash;
        }

        public BundleInfo Clone()
        {
            return new BundleInfo
            {
                Version = Version, Hash = Hash, ProfileVersion = ProfileVersion, ProfileHash = ProfileHash
            };
        }

        public void Validate()
        {
            SharingWire.ValidateHash(Version);
            SharingWire.ValidateHash(Hash);
            SharingWire.ValidateHash(ProfileVersion);
            SharingWire.ValidateHash(ProfileHash);
        }

        public void Write(BinaryWriter writer)
        {
            SharingWire.WriteText(writer, Version ?? "");
            SharingWire.WriteText(writer, Hash ?? "");
            SharingWire.WriteText(writer, ProfileVersion ?? "");
            SharingWire.WriteText(writer, ProfileHash ?? "");
        }

        // A stored manifest may hold only one of the two blobs; each present pair must be valid.
        public static BundleInfo Read(BinaryReader reader)
        {
            var info = new BundleInfo
            {
                Version = SharingWire.ReadText(reader),
                Hash = SharingWire.ReadText(reader),
                ProfileVersion = SharingWire.ReadText(reader),
                ProfileHash = SharingWire.ReadText(reader)
            };
            if (info.Version != "" || info.Hash != "")
            {
                SharingWire.ValidateHash(info.Version);
                SharingWire.ValidateHash(info.Hash);
            }

            if (info.ProfileVersion != "" || info.ProfileHash != "")
            {
                SharingWire.ValidateHash(info.ProfileVersion);
                SharingWire.ValidateHash(info.ProfileHash);
            }

            return info;
        }
    }
}
