using System.Security.Cryptography;
using System.Text;
using Uqeb.Tools.DatabaseProvision;
using Xunit;

namespace Uqeb.Tools.DatabaseProvision.Tests;

public class ProvisionBackupHashTests
{
    [Fact]
    public async Task ComputeFileSha256HexAsync_UsesStreamingHash_ForLargeFile()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"uqeb-bak-hash-{Guid.NewGuid():N}.bin");
        try
        {
            await using (var stream = File.Create(tempPath))
            {
                var chunk = Encoding.UTF8.GetBytes(new string('a', 8192));
                for (var i = 0; i < 512; i++)
                {
                    await stream.WriteAsync(chunk);
                }
            }

            await using var verifyStream = File.OpenRead(tempPath);
            var expected = Convert.ToHexString(await SHA256.HashDataAsync(verifyStream));
            var actual = await ProvisionExecution.ComputeFileSha256HexAsync(tempPath);

            Assert.Equal(expected, actual, ignoreCase: true);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }
}
