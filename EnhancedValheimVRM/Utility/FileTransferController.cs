using System;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using UnityEngine;

namespace EnhancedValheimVRM
{
    public class FileTransferController
    {
        public void GetVrmFromPlayer()
        {
            // Retrieve VRM instance and related file paths
            var vrmInstance = Player.m_localPlayer.GetVrmInstance();
            var settingsFilePath = vrmInstance.GetSettingsFilePath();
            var vrmFilePath = vrmInstance.GetVrmFilePath();

            // Create ZIP file containing the settings and VRM file
            var zipFilePath = CreateZip(settingsFilePath, vrmFilePath);

            // Encrypt the ZIP file
            var encryptedBundlePath = EncryptBundle(zipFilePath);

            // Generate hash for the encrypted bundle
            var hash = GetFileHash(encryptedBundlePath);
            SaveHash(Player.m_localPlayer.GetPlayerDisplayName(), hash);

            // Rename the encrypted bundle using the hash
            var bundleName = $"{hash}.bundle";
            var renamedBundlePath = Path.Combine(Path.GetDirectoryName(encryptedBundlePath), bundleName);
            File.Move(encryptedBundlePath, renamedBundlePath);
        }

        private string CreateZip(string textFilePath, string vrmFilePath)
        {
            string zipFilePath = "";

            using (FileStream zipToOpen = new FileStream(zipFilePath, FileMode.Create))
            {
                using (ZipArchive archive = new ZipArchive(zipToOpen, ZipArchiveMode.Update))
                {
                    ZipArchiveEntry textFileEntry = archive.CreateEntry(Path.GetFileName(textFilePath));
                    using (StreamWriter writer = new StreamWriter(textFileEntry.Open()))
                    {
                        writer.Write(File.ReadAllText(textFilePath));
                    }

                    ZipArchiveEntry vrmFileEntry = archive.CreateEntry(Path.GetFileName(vrmFilePath));
                    using (Stream vrmFileStream = vrmFileEntry.Open())
                    {
                        using (FileStream fileStream = new FileStream(vrmFilePath, FileMode.Open))
                        {
                            fileStream.CopyTo(vrmFileStream);
                        }
                    }
                }
            }

            return zipFilePath;
        }

        private string EncryptBundle(string zipFilePath)
        {
            string encryptedZipFilePath = "";

            byte[] keyBytes = Convert.FromBase64String(Settings.VrmKey);
            byte[] ivBytes = Convert.FromBase64String(Settings.VrmIv);

            using (Aes aesAlg = Aes.Create())
            {
                aesAlg.Key = keyBytes;
                aesAlg.IV = ivBytes;

                ICryptoTransform encryptor = aesAlg.CreateEncryptor(aesAlg.Key, aesAlg.IV);

                using (FileStream fsOutput = new FileStream(encryptedZipFilePath, FileMode.Create))
                {
                    using (CryptoStream csEncrypt = new CryptoStream(fsOutput, encryptor, CryptoStreamMode.Write))
                    {
                        using (FileStream fsInput = new FileStream(zipFilePath, FileMode.Open))
                        {
                            fsInput.CopyTo(csEncrypt);
                        }
                    }
                }
            }

            return encryptedZipFilePath;
        }

        private string GetFileHash(string filePath)
        {
            using (var sha256 = SHA256.Create())
            {
                using (var stream = File.OpenRead(filePath))
                {
                    var hashBytes = sha256.ComputeHash(stream);
                    return BitConverter.ToString(hashBytes).Replace("-", "").ToLower();
                }
            }
        }

        private void SaveHash(string username, string hash)
        {
            string hashFilePath = Settings.CharacterFile;
            string newLine = $"{username}:{hash}";
            bool userFound = false;

            // Read the existing file content
            var lines = File.Exists(hashFilePath) ? File.ReadAllLines(hashFilePath) : Array.Empty<string>();

            // Check if the username already exists and update the hash if needed
            for (int i = 0; i < lines.Length; i++)
            {
                var parts = lines[i].Split(':');
                if (parts[0] == username)
                {
                    userFound = true;
                    if (parts[1] != hash)
                    {
                        lines[i] = newLine;
                    }
                    break;
                }
            }

            // If the user was not found, add the new line
            if (!userFound)
            {
                using (StreamWriter sw = File.AppendText(hashFilePath))
                {
                    sw.WriteLine(newLine);
                }
            }
            else
            {
                // Write updated content back to the file
                File.WriteAllLines(hashFilePath, lines);
            }
        }

        public void RPC_SendVrmBundleToPlayer()
        {
            var encryptedBundle = GetEncryptedVrmBundle();
            RPC_SendBundleToPlayer(encryptedBundle);
            throw new NotImplementedException();
        }

        private byte[] GetEncryptedVrmBundle()
        {
            throw new NotImplementedException();
        }

        private void RPC_SendBundleToPlayer(byte[] encryptedBundle)
        {
            throw new NotImplementedException();
        }

        public void RPC_RequestKey()
        {
            SendKeyToPlayer(Settings.VrmKey);
            throw new NotImplementedException();
        }

        private void SendKeyToPlayer(string key)
        {
            throw new NotImplementedException();
        }
    }
}
