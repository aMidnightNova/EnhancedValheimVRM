using System.IO;
using System.IO.Compression;
using System.Threading.Tasks;

namespace EnhancedValheimVRM
{
    public static class Compression
    {
        public static byte[] CompressBytes(byte[] inputBytes)
        {
            using (var outputMemoryStream = new MemoryStream())
            {
                using (var gzipStream = new GZipStream(outputMemoryStream, CompressionMode.Compress))
                {
                    gzipStream.Write(inputBytes, 0, inputBytes.Length);
                }

                return outputMemoryStream.ToArray();
            }
        }

        public static async Task<byte[]> CompressBytesAsync(byte[] inputBytes)
        {
            using (var outputMemoryStream = new MemoryStream())
            {
                using (var gzipStream = new GZipStream(outputMemoryStream, CompressionMode.Compress, leaveOpen: true))
                {
                    await gzipStream.WriteAsync(inputBytes, 0, inputBytes.Length);
                }

                return outputMemoryStream.ToArray();
            }
        }

        public static byte[] DecompressBytes(byte[] inputBytes)
        {
            using (var inputMemoryStream = new MemoryStream(inputBytes))
            {
                using (var outputMemoryStream = new MemoryStream())
                {
                    using (var gzipStream = new GZipStream(inputMemoryStream, CompressionMode.Decompress))
                    {
                        gzipStream.CopyTo(outputMemoryStream);
                    }

                    return outputMemoryStream.ToArray();
                }
            }
        }

        public static async Task<byte[]> DecompressBytesAsync(byte[] inputBytes)
        {
            using (var inputMemoryStream = new MemoryStream(inputBytes))
            {
                using (var outputMemoryStream = new MemoryStream())
                {
                    using (var gzipStream = new GZipStream(inputMemoryStream, CompressionMode.Decompress))
                    {
                        await gzipStream.CopyToAsync(outputMemoryStream);
                    }

                    return outputMemoryStream.ToArray();
                }
            }
        }
    }
}