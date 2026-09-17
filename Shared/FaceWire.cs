using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace EnhancedValheimVRM.Sharing
{
    // Face packets contain 52 ARKit blendshape weights and 14 lipsync visemes.
    // Clients encrypt and authenticate the payload; the relay reads only the plain header.
    public static class FaceWire
    {
        public const byte Face = 4;
        public const byte Version = 1;
        public const int ArkitCount = 52;
        public const int ValueCount = 66;
        public const int CipherBytes = 80; // 66 values, pkcs7 padded to the block
        public const int TagBytes = 32;
        public const int HeaderBytes = 8 + 1 + 1 + 4 + 4; // id, version, rate, nonce, sequence
        public const int PacketBytes = HeaderBytes + CipherBytes + TagBytes;
        public const int KeepaliveBytes = 9;
        public const int MaxTicketBytes = 64;

        // canonical arkit order, then the viseme names.
        // never reorder, only append and bump Version
        public static readonly string[] Catalogue =
        {
            "eyeBlinkLeft", "eyeLookDownLeft", "eyeLookInLeft", "eyeLookOutLeft", "eyeLookUpLeft", "eyeSquintLeft",
            "eyeWideLeft", "eyeBlinkRight", "eyeLookDownRight", "eyeLookInRight", "eyeLookOutRight",
            "eyeLookUpRight", "eyeSquintRight", "eyeWideRight", "jawForward", "jawLeft", "jawRight", "jawOpen",
            "mouthClose", "mouthFunnel", "mouthPucker", "mouthLeft", "mouthRight", "mouthSmileLeft",
            "mouthSmileRight", "mouthFrownLeft", "mouthFrownRight", "mouthDimpleLeft", "mouthDimpleRight",
            "mouthStretchLeft", "mouthStretchRight", "mouthRollLower", "mouthRollUpper", "mouthShrugLower",
            "mouthShrugUpper", "mouthPressLeft", "mouthPressRight", "mouthLowerDownLeft", "mouthLowerDownRight",
            "mouthUpperUpLeft", "mouthUpperUpRight", "browDownLeft", "browDownRight", "browInnerUp",
            "browOuterUpLeft", "browOuterUpRight", "cheekPuff", "cheekSquintLeft", "cheekSquintRight",
            "noseSneerLeft", "noseSneerRight", "tongueOut", "PP", "FF", "TH", "DD", "kk", "CH", "SS", "nn", "RR",
            "aa", "E", "ih", "oh", "ou"
        };

        // the mesh shape name a viseme usually goes by, null for the arkit part
        public static string MeshAlias(int index)
        {
            return index < ArkitCount ? null : "vrc.v_" + Catalogue[index].ToLowerInvariant();
        }

        public static int IndexOf(string name)
        {
            for (var i = 0; i < Catalogue.Length; i++)
            {
                if (string.Equals(Catalogue[i], name, StringComparison.OrdinalIgnoreCase)) return i;
            }

            return -1;
        }

        public static byte Quantize(float value)
        {
            if (float.IsNaN(value)) return 0;
            return (byte)Math.Round(Math.Min(1f, Math.Max(0f, value)) * 255f);
        }

        // hello: magic, Face, character id, ticket. the relay learns the senders endpoint from it
        public static byte[] BuildHello(long characterId, string ticket)
        {
            SharingWire.ValidateId(characterId);
            var text = Encoding.ASCII.GetBytes(ticket ?? "");
            if (text.Length == 0 || text.Length > MaxTicketBytes)
                throw new InvalidDataException("Invalid face ticket.");
            var packet = new byte[4 + 1 + 8 + 1 + text.Length];
            Array.Copy(BitConverter.GetBytes(SharingWire.Magic), 0, packet, 0, 4);
            packet[4] = Face;
            Array.Copy(BitConverter.GetBytes(characterId), 0, packet, 5, 8);
            packet[13] = (byte)text.Length;
            Array.Copy(text, 0, packet, 14, text.Length);
            return packet;
        }

        public static bool TryParseHello(byte[] packet, int length, out long characterId, out string ticket)
        {
            characterId = 0;
            ticket = null;
            if (length < 15 || length > 14 + MaxTicketBytes || BitConverter.ToInt32(packet, 0) != SharingWire.Magic ||
                packet[4] != Face)
                return false;
            var textLength = packet[13];
            if (textLength == 0 || 14 + textLength != length) return false;
            characterId = BitConverter.ToInt64(packet, 5);
            if (characterId == 0) return false;
            ticket = Encoding.ASCII.GetString(packet, 14, textLength);
            foreach (var c in ticket)
            {
                if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f'))) return false;
            }

            return true;
        }

        public static readonly byte[] Ack = { 0x45, 0x56, 0x52, 0x32, Face, 1 };

        public static bool IsAck(byte[] packet, int length)
        {
            if (length != Ack.Length) return false;
            for (var i = 0; i < Ack.Length; i++)
            {
                if (packet[i] != Ack[i]) return false;
            }

            return true;
        }

        public static byte[] BuildKeepalive(long characterId)
        {
            var packet = new byte[KeepaliveBytes];
            Array.Copy(BitConverter.GetBytes(characterId), 0, packet, 0, 8);
            packet[8] = 0;
            return packet;
        }

        // the part the relay reads: who sent it and whether it is a keepalive or a face packet
        public static bool TryPeek(byte[] packet, int length, out long characterId, out bool keepalive)
        {
            characterId = 0;
            keepalive = false;
            if (length != KeepaliveBytes && length != PacketBytes) return false;
            characterId = BitConverter.ToInt64(packet, 0);
            if (characterId == 0) return false;
            keepalive = packet[8] == 0;
            if (keepalive) return length == KeepaliveBytes;
            return packet[8] == Version && length == PacketBytes;
        }

        // frames per second the sender is sending at, so a viewer knows how long a move gets
        public static byte ReadRate(byte[] packet)
        {
            return packet[9];
        }

        public static uint ReadNonce(byte[] packet)
        {
            return BitConverter.ToUInt32(packet, 10);
        }

        public static uint ReadSequence(byte[] packet)
        {
            return BitConverter.ToUInt32(packet, 14);
        }
    }

    // seals and opens face packets for one owner key. keys are derived once, the hmac and aes
    // derive keys once and reuse the crypto objects. calls on the same instance must not overlap.
    public sealed class FaceCipher : IDisposable
    {
        private readonly Aes _aes = Aes.Create();
        private readonly HMACSHA256 _tag, _iv;
        private readonly byte[] _ivInput = new byte[8];

        public FaceCipher(string ownerKey)
        {
            _aes.Key = BundleCrypto.Derive(ownerKey, "face");
            _aes.Mode = CipherMode.CBC;
            _aes.Padding = PaddingMode.PKCS7;
            _tag = new HMACSHA256(BundleCrypto.Derive(ownerKey, "authentication"));
            _iv = new HMACSHA256(BundleCrypto.Derive(ownerKey, "face-iv"));
        }

        private byte[] Iv(uint nonce, uint sequence)
        {
            Array.Copy(BitConverter.GetBytes(nonce), 0, _ivInput, 0, 4);
            Array.Copy(BitConverter.GetBytes(sequence), 0, _ivInput, 4, 4);
            var iv = new byte[16];
            Array.Copy(_iv.ComputeHash(_ivInput), iv, 16);
            return iv;
        }

        public byte[] Seal(long characterId, byte rate, uint nonce, uint sequence, byte[] values)
        {
            SharingWire.ValidateId(characterId);
            if (values == null || values.Length != FaceWire.ValueCount)
                throw new ArgumentException("A face packet carries " + FaceWire.ValueCount + " values.");
            if (rate < 15 || rate > 60) throw new ArgumentException("Face rate is 15 to 60 frames per second.");
            var packet = new byte[FaceWire.PacketBytes];
            Array.Copy(BitConverter.GetBytes(characterId), 0, packet, 0, 8);
            packet[8] = FaceWire.Version;
            packet[9] = rate;
            Array.Copy(BitConverter.GetBytes(nonce), 0, packet, 10, 4);
            Array.Copy(BitConverter.GetBytes(sequence), 0, packet, 14, 4);
            _aes.IV = Iv(nonce, sequence);
            using (var encryptor = _aes.CreateEncryptor())
            {
                var cipher = encryptor.TransformFinalBlock(values, 0, values.Length);
                if (cipher.Length != FaceWire.CipherBytes)
                    throw new CryptographicException("Unexpected face packet size.");
                Array.Copy(cipher, 0, packet, FaceWire.HeaderBytes, FaceWire.CipherBytes);
            }

            var tag = _tag.ComputeHash(packet, 0, FaceWire.HeaderBytes + FaceWire.CipherBytes);
            Array.Copy(tag, 0, packet, FaceWire.HeaderBytes + FaceWire.CipherBytes, FaceWire.TagBytes);
            return packet;
        }

        // false when the tag does not match. values is filled only on success
        public bool Open(byte[] packet, int length, byte[] values)
        {
            if (length != FaceWire.PacketBytes || packet[8] != FaceWire.Version) return false;
            var signed = FaceWire.HeaderBytes + FaceWire.CipherBytes;
            var expected = _tag.ComputeHash(packet, 0, signed);
            var difference = 0;
            for (var i = 0; i < FaceWire.TagBytes; i++) difference |= expected[i] ^ packet[signed + i];
            if (difference != 0) return false;
            _aes.IV = Iv(FaceWire.ReadNonce(packet), FaceWire.ReadSequence(packet));
            try
            {
                using (var decryptor = _aes.CreateDecryptor())
                {
                    var plain = decryptor.TransformFinalBlock(packet, FaceWire.HeaderBytes, FaceWire.CipherBytes);
                    if (plain.Length != FaceWire.ValueCount) return false;
                    Array.Copy(plain, values, FaceWire.ValueCount);
                }
            }
            catch (CryptographicException)
            {
                return false;
            }

            return true;
        }

        public void Dispose()
        {
            _aes.Dispose();
            _tag.Dispose();
            _iv.Dispose();
        }
    }

    // per sender replay guard. a packet counts once: newer than anything seen goes through, one that
// is older by less than the window goes through if it was not seen yet (udp reorders), older
// than the window or seen before is dropped. a session nonce never comes back once it has been left
    public sealed class FaceReplayGuard
    {
        public const int Window = 64;

        private readonly System.Collections.Generic.HashSet<uint> _pastNonces =
            new System.Collections.Generic.HashSet<uint>();

        private uint _nonce, _highest;
        private ulong _seen; // bit n set: highest - n was seen
        private bool _started;

        public bool Accept(uint nonce, uint sequence)
        {
            if (!_started || nonce != _nonce)
            {
                if (_started && _pastNonces.Contains(nonce)) return false;
                if (_started)
                {
                    if (_pastNonces.Count >= 256) _pastNonces.Clear();
                    _pastNonces.Add(_nonce);
                }

                _started = true;
                _nonce = nonce;
                _highest = sequence;
                _seen = 1;
                return true;
            }

            // signed distance forward, allowing for wrap
            var ahead = (int)(sequence - _highest);
            if (ahead > 0)
            {
                _seen = ahead >= Window ? 1 : (_seen << ahead) | 1;
                _highest = sequence;
                return true;
            }

            var behind = -ahead;
            if (behind >= Window) return false;
            var bit = 1UL << behind;
            if ((_seen & bit) != 0) return false;
            _seen |= bit;
            return true;
        }
    }
}
