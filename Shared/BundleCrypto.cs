using System;
using System.IO;
using System.IO.Compression;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace EnhancedValheimVRM.Sharing
{
    public sealed class AvatarBundle
    {
        public long CharacterId;
        public byte[] Vrm;
        public string Settings;

        public string Outfits = "";

        // Runtime-only metadata, set by Receive after all authentication/version checks.
        internal string VerifiedVersion, ProfileVersion;
    }

    public static class BundleCrypto
    {
        public static string GenerateKey()
        {
            var bytes = new byte[32];
            using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(bytes);
            return Convert.ToBase64String(bytes);
        }

        public static bool IsValidKey(string key)
        {
            try
            {
                return Convert.FromBase64String(key ?? "").Length == 32;
            }
            catch (FormatException)
            {
                return false;
            }
        }

        public static string Hash(byte[] bytes)
        {
            using (var sha = SHA256.Create()) return Hex(sha.ComputeHash(bytes));
        }

        private static string Hex(byte[] bytes)
        {
            return BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant();
        }

        private static byte[] Derive(string key, string purpose)
        {
            if (!IsValidKey(key))
            {
                throw new CryptographicException(
                    "Your sharing secret is invalid; clear Security.VrmKey in the config to regenerate it.");
            }

            using (var hmac = new HMACSHA256(Convert.FromBase64String(key)))
                return hmac.ComputeHash(Encoding.ASCII.GetBytes("EnhancedValheimVRM/v1/" + purpose));
        }

        // Fixed order/timestamps keep identical source files at the same bundle version.
        private static readonly DateTimeOffset ZipTimestamp = new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);
        private static readonly Encoding Utf8 = new UTF8Encoding(false, true);

        // The avatar blob holds only the model, so its version changes only when the file does.
        public static byte[] PackAvatar(byte[] vrm)
        {
            if (vrm == null || vrm.Length == 0 || vrm.Length > SharingWire.MaxExpandedVrmBytes)
                throw new InvalidDataException("VRM exceeds the bundle limit or is empty.");
            return Zip(archive => WriteEntry(archive, "avatar.vrm", vrm));
        }

        // The profile blob holds settings and outfits; it is small and re-sent on every change.
        public static byte[] PackProfile(string settings, string outfits)
        {
            if (Utf8.GetByteCount(settings ?? "") > SharingWire.MaxSettingsBytes)
                throw new InvalidDataException("Settings exceed the size limit.");
            if (Utf8.GetByteCount(outfits ?? "") > SharingWire.MaxSettingsBytes)
                throw new InvalidDataException("Outfits exceed the size limit.");
            return Zip(archive =>
            {
                WriteTextEntry(archive, "settings.txt", settings ?? "");
                WriteTextEntry(archive, "outfits.txt", outfits ?? "");
            });
        }

        private static byte[] Zip(Action<ZipArchive> write)
        {
            using (var stream = new MemoryStream())
            {
                try
                {
                    using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, true)) write(archive);
                    if (stream.Length > SharingWire.MaxBundleBytes - 64)
                        throw new InvalidDataException("ZIP exceeds the encrypted bundle limit.");
                    return stream.ToArray();
                }
                finally
                {
                    // MemoryStream.Dispose alone does not erase its backing array.
                    var buffer = stream.GetBuffer();
                    Array.Clear(buffer, 0, buffer.Length);
                }
            }
        }

        private static void WriteEntry(ZipArchive archive, string name, byte[] bytes)
        {
            var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
            entry.LastWriteTime = ZipTimestamp;
            using (var output = entry.Open()) output.Write(bytes, 0, bytes.Length);
        }

        private static void WriteTextEntry(ZipArchive archive, string name, string text)
        {
            var bytes = Utf8.GetBytes(text);
            try
            {
                WriteEntry(archive, name, bytes);
            }
            finally
            {
                ClearBytes(ref bytes);
            }
        }

        public static byte[] UnpackAvatar(byte[] bytes)
        {
            using (var archive = OpenZip(bytes, 1))
            {
                var avatar = archive.Entries[0];
                if (avatar.FullName != "avatar.vrm" || avatar.Length == 0)
                    throw new InvalidDataException("Missing bundle file.");
                return ReadEntry(avatar, SharingWire.MaxExpandedVrmBytes);
            }
        }

        public static void UnpackProfile(byte[] bytes, out string settings, out string outfits)
        {
            using (var archive = OpenZip(bytes, 2))
            {
                ZipArchiveEntry settingsEntry = null, outfitsEntry = null;
                foreach (var entry in archive.Entries)
                {
                    switch (entry.FullName)
                    {
                        case "settings.txt":
                            if (settingsEntry != null) throw new InvalidDataException("Duplicate settings.");
                            settingsEntry = entry;
                            break;
                        case "outfits.txt":
                            if (outfitsEntry != null) throw new InvalidDataException("Duplicate outfits.");
                            outfitsEntry = entry;
                            break;
                        default:
                            throw new InvalidDataException("Unexpected ZIP entry.");
                    }
                }

                if (settingsEntry == null || outfitsEntry == null)
                    throw new InvalidDataException("Missing bundle file.");
                settings = ReadTextEntry(settingsEntry, SharingWire.MaxSettingsBytes);
                outfits = ReadTextEntry(outfitsEntry, SharingWire.MaxSettingsBytes);
            }
        }

        private static ZipArchive OpenZip(byte[] bytes, int entries)
        {
            if (bytes == null || bytes.Length > SharingWire.MaxBundleBytes)
                throw new InvalidDataException("Invalid ZIP size.");
            var archive = new ZipArchive(new MemoryStream(bytes, false), ZipArchiveMode.Read, false);
            try
            {
                if (archive.Entries.Count != entries)
                    throw new InvalidDataException("Bundle must contain exactly " + entries + " file(s).");
                return archive;
            }
            catch
            {
                archive.Dispose();
                throw;
            }
        }

        private static void ValidateEntrySize(ZipArchiveEntry entry, int maximum)
        {
            if (entry.Length < 0 || entry.Length > maximum)
                throw new InvalidDataException("ZIP entry exceeds the size limit.");
        }

        private static byte[] ReadEntry(ZipArchiveEntry entry, int maximum)
        {
            ValidateEntrySize(entry, maximum);
            var result = new byte[(int)entry.Length];
            try
            {
                using (var input = entry.Open())
                {
                    var offset = 0;
                    while (offset < result.Length)
                    {
                        var count = input.Read(result, offset, Math.Min(65536, result.Length - offset));
                        if (count == 0) throw new EndOfStreamException("Truncated ZIP entry.");
                        offset += count;
                    }

                    if (input.ReadByte() != -1) throw new InvalidDataException("ZIP entry size mismatch.");
                }

                return result;
            }
            catch
            {
                ClearBytes(ref result);
                throw;
            }
        }

        private static string ReadTextEntry(ZipArchiveEntry entry, int maximum)
        {
            var bytes = ReadEntry(entry, maximum);
            try
            {
                return Utf8.GetString(bytes);
            }
            finally
            {
                ClearBytes(ref bytes);
            }
        }

        internal static void ClearBytes(ref byte[] bytes)
        {
            if (bytes == null) return;
            Array.Clear(bytes, 0, bytes.Length);
            bytes = null;
        }

        // Version is deterministic over the ZIP bytes and changes on key rotation.
        public static string Version(byte[] plaintext, string key)
        {
            using (var hmac = new HMACSHA256(Derive(key, "zip-v2-version"))) return Hex(hmac.ComputeHash(plaintext));
        }

        // The tag binds the ciphertext to its announced version AND its character, so a blob
        // cannot be presented under another version or as another character's.
        public static byte[] Encrypt(byte[] plaintext, string key, string version, long characterId)
        {
            SharingWire.ValidateHash(version);
            SharingWire.ValidateId(characterId);
            using (var aes = Aes.Create())
            {
                aes.Key = Derive(key, "encryption");
                aes.GenerateIV();
                var cipherLength = (plaintext.Length / 16 + 1) * 16;
                var encrypted = new byte[16 + cipherLength + 32];
                Array.Copy(aes.IV, encrypted, 16);
                using (var output = new MemoryStream(encrypted, 16, cipherLength, true))
                using (var encryptor = aes.CreateEncryptor())
                using (var crypto = new CryptoStream(output, encryptor, CryptoStreamMode.Write))
                {
                    crypto.Write(plaintext, 0, plaintext.Length);
                    crypto.FlushFinalBlock();
                }

                var tag = Authenticate(encrypted, encrypted.Length - 32, key, version, characterId);
                Array.Copy(tag, 0, encrypted, encrypted.Length - 32, 32);
                return encrypted;
            }
        }

        private static byte[] Authenticate(byte[] encrypted, int count, string key, string version, long characterId)
        {
            SharingWire.ValidateHash(version);
            using (var hmac = new HMACSHA256(Derive(key, "authentication")))
            {
                hmac.TransformBlock(encrypted, 0, count, null, 0);
                var identity =
                    Encoding.ASCII.GetBytes(version + ":" + characterId.ToString(CultureInfo.InvariantCulture));
                hmac.TransformFinalBlock(identity, 0, identity.Length);
                return hmac.Hash;
            }
        }

        public static byte[] Decrypt(byte[] bundle, string key, string version, long characterId)
        {
            return DecryptTimed(bundle, key, version, characterId, null);
        }

        internal static byte[] DecryptTimed(byte[] bundle,
            string key,
            string version,
            long characterId,
            Action<string> timing)
        {
            var clock = timing == null ? null : System.Diagnostics.Stopwatch.StartNew();
            if (bundle.Length < 64 || bundle.Length > SharingWire.MaxBundleBytes)
                throw new InvalidDataException("Invalid encrypted bundle size.");
            var authenticatedLength = bundle.Length - 32;
            var expected = Authenticate(bundle, authenticatedLength, key, version, characterId);
            var difference = 0;
            for (var i = 0; i < expected.Length; i++) difference |= expected[i] ^ bundle[authenticatedLength + i];
            if (difference != 0) throw new CryptographicException("Bundle authentication failed.");
            var verifyMs = clock?.Elapsed.TotalMilliseconds ?? 0;
            clock?.Restart();
            using (var aes = Aes.Create())
            {
                aes.Key = Derive(key, "encryption");
                var iv = new byte[16];
                Array.Copy(bundle, iv, iv.Length);
                aes.IV = iv;
                byte[] plaintext;
                using (var decryptor = aes.CreateDecryptor())
                    plaintext = decryptor.TransformFinalBlock(bundle, 16, authenticatedLength - 16);
                timing?.Invoke("verified in " + verifyMs.ToString("F0", CultureInfo.InvariantCulture) +
                    "ms; unpacked in " + clock.Elapsed.TotalMilliseconds.ToString("F0", CultureInfo.InvariantCulture) +
                    "ms");
                return plaintext;
            }
        }
    }
}
