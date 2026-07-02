using System.Globalization;
using System.Resources;
using GobchatEx.Localization;

namespace GobchatEx.Core.Tests;

/// <summary>
/// Exercises Loc.Resolve against a small test-only resx fixture
/// (Fixtures/LocFixture.resx + LocFixture.de.resx), not the real
/// GobchatEx/Resources/Language.resx — that one isn't embedded in this
/// assembly (see ADR 0002: no reference to GobchatEx.dll here).
/// </summary>
public sealed class LocTests
{
    private static readonly ResourceManager Fixture =
        new("GobchatEx.Core.Tests.Fixtures.LocFixture", typeof(LocTests).Assembly);

    [Fact]
    public void Resolve_ReturnsEnglishString_ForKnownKeyUnderNeutralCulture()
    {
        // Arrange / Act
        var result = Loc.Resolve(Fixture, CultureInfo.InvariantCulture, "Greeting");

        // Assert
        result.Should().Be("Hello");
    }

    [Fact]
    public void Resolve_UsesGermanTranslation_WhenKeyExistsInGermanSatellite()
    {
        // Arrange / Act
        var result = Loc.Resolve(Fixture, new CultureInfo("de"), "Greeting");

        // Assert
        result.Should().Be("Hallo");
    }

    [Fact]
    public void Resolve_FallsBackToNeutral_WhenKeyIsMissingFromGermanSatellite()
    {
        // Arrange / Act — "OnlyInNeutral" is deliberately absent from LocFixture.de.resx.
        var result = Loc.Resolve(Fixture, new CultureInfo("de"), "OnlyInNeutral");

        // Assert
        result.Should().Be("Only in neutral");
    }

    [Fact]
    public void Resolve_ReturnsTheKeyItself_WhenTheKeyIsMissingEverywhere()
    {
        // Arrange / Act
        var result = Loc.Resolve(Fixture, CultureInfo.InvariantCulture, "NoSuchKey");

        // Assert
        result.Should().Be("NoSuchKey");
    }
}
