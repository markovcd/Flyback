using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Flyback.Tests;
using Shouldly;

namespace Flyback.Core.Tests.Architecture;

/// <summary>
/// The certificate the Android APK is signed under holds the key every update is checked against (ADR-0198).
/// </summary>
[TestCategory(TestCategory.Unmeasured)]
public class AndroidCertificateTests
{
    [Fact]
    public void The_Android_certificate_holds_the_release_key()
    {
        using var certificate = X509Certificate2.CreateFromPem(File.ReadAllText(Repository.Path("src", "Flyback.Editor.Android", "release-cert.pem")));
        using var release = ECDsa.Create();
        release.ImportFromPem(File.ReadAllText(Repository.Path("src", "Flyback.Editor", "Updates", "release-key.pem")));

        certificate.PublicKey.ExportSubjectPublicKeyInfo()
            .ShouldBe(release.ExportSubjectPublicKeyInfo(), "release-cert.pem is made from RELEASE_SIGNING_KEY, the private half of release-key.pem");
    }
}
