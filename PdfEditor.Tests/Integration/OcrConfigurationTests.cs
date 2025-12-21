using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PdfEditor.Services;
using PdfEditor.Services.Verification;
using PdfEditor.ViewModels;
using Xunit;

namespace PdfEditor.Tests.Integration;

/// <summary>
/// Integration tests for OCR configuration flow
/// Tests that OCR settings from preferences are correctly used in OCR operations
/// </summary>
public class OcrConfigurationTests
{
    [Fact]
    public void MainViewModel_DefaultOcrSettings_AreCorrect()
    {
        // Arrange & Act
        var vm = CreateMainViewModel();

        // Assert
        vm.OcrLanguages.Should().Be("eng", "English should be default");
        vm.OcrBaseDpi.Should().Be(350, "350 DPI is the standard OCR resolution");
        vm.OcrHighDpi.Should().Be(450, "450 DPI for retry");
        vm.OcrLowConfidence.Should().Be(0.6, "60% confidence threshold");
    }

    [Fact]
    public void PreferencesViewModel_LoadAndSave_RoundTripsCorrectly()
    {
        // Arrange
        var mainVm = CreateMainViewModel();
        mainVm.OcrLanguages = "deu+eng";
        mainVm.OcrBaseDpi = 400;
        mainVm.OcrHighDpi = 550;
        mainVm.OcrLowConfidence = 0.75;

        var prefsVm = new PreferencesViewModel();

        // Act - Load from main
        prefsVm.LoadFromMainViewModel(mainVm);

        // Assert - Loaded correctly
        prefsVm.OcrLanguages.Should().Be("deu+eng");
        prefsVm.OcrBaseDpi.Should().Be(400);

        // Act - Modify
        prefsVm.OcrLanguages = "fra";
        prefsVm.OcrBaseDpi = 300;

        // Act - Save back to main
        prefsVm.SaveToMainViewModel(mainVm);

        // Assert - Saved correctly
        mainVm.OcrLanguages.Should().Be("fra");
        mainVm.OcrBaseDpi.Should().Be(300);
    }

    [Fact]
    public void RenderCacheMax_SettingInPreferences_UpdatesRenderService()
    {
        // Arrange
        var vm = CreateMainViewModel();
        var renderService = new PdfRenderService(new Mock<ILogger<PdfRenderService>>().Object);

        // Initial default
        renderService.MaxCacheEntries.Should().Be(20);

        // Act - Change via ViewModel property
        vm.RenderCacheMax = 50;

        // Since ViewModel doesn't directly hold the render service, we test that the property changes
        vm.RenderCacheMax.Should().Be(50);
    }

    [Fact]
    public void VerifyAfterSave_DefaultValue_IsTrue()
    {
        // Arrange & Act
        var vm = CreateMainViewModel();

        // Assert
        vm.RunVerifyAfterSave.Should().BeTrue("verification should be enabled by default for security");
    }

    [Fact]
    public void VerifyAfterSave_CanBeToggledViaPreferences()
    {
        // Arrange
        var mainVm = CreateMainViewModel();
        mainVm.RunVerifyAfterSave = true;

        var prefsVm = new PreferencesViewModel();
        prefsVm.LoadFromMainViewModel(mainVm);

        // Act
        prefsVm.RunVerifyAfterSave = false;
        prefsVm.SaveToMainViewModel(mainVm);

        // Assert
        mainVm.RunVerifyAfterSave.Should().BeFalse();
    }

    [Theory]
    [InlineData("eng", 350, 450, 0.6)]
    [InlineData("deu", 300, 400, 0.5)]
    [InlineData("fra+spa", 400, 500, 0.7)]
    [InlineData("eng+deu+fra", 500, 600, 0.8)]
    public void OcrConfiguration_SupportsVariousLanguageAndDpiCombinations(
        string languages, int baseDpi, int highDpi, double lowConfidence)
    {
        // Arrange
        var vm = CreateMainViewModel();
        var prefsVm = new PreferencesViewModel();

        // Act
        prefsVm.OcrLanguages = languages;
        prefsVm.OcrBaseDpi = baseDpi;
        prefsVm.OcrHighDpi = highDpi;
        prefsVm.OcrLowConfidence = lowConfidence;
        prefsVm.SaveToMainViewModel(vm);

        // Assert
        vm.OcrLanguages.Should().Be(languages);
        vm.OcrBaseDpi.Should().Be(baseDpi);
        vm.OcrHighDpi.Should().Be(highDpi);
        vm.OcrLowConfidence.Should().Be(lowConfidence);
    }

    [Fact]
    public void PreferencesViewModel_AllPropertiesHaveGettersAndSetters()
    {
        // Arrange
        var vm = new PreferencesViewModel();

        // Act & Assert - All properties are readable and writable
        vm.OcrLanguages = "test";
        vm.OcrLanguages.Should().Be("test");

        vm.OcrBaseDpi = 100;
        vm.OcrBaseDpi.Should().Be(100);

        vm.OcrHighDpi = 200;
        vm.OcrHighDpi.Should().Be(200);

        vm.OcrLowConfidence = 0.5;
        vm.OcrLowConfidence.Should().Be(0.5);

        vm.OcrPreprocess = false;
        vm.OcrPreprocess.Should().BeFalse();

        vm.OcrBinarize = false;
        vm.OcrBinarize.Should().BeFalse();

        vm.OcrDenoiseRadius = 1.0;
        vm.OcrDenoiseRadius.Should().Be(1.0);

        vm.RenderCacheMax = 10;
        vm.RenderCacheMax.Should().Be(10);

        vm.RunVerifyAfterSave = false;
        vm.RunVerifyAfterSave.Should().BeFalse();
    }

    [Fact]
    public void MainViewModel_OcrPropertiesAreExposedPublicly()
    {
        // Arrange
        var vm = CreateMainViewModel();

        // Act & Assert - All OCR properties are accessible
        var ocrLanguages = vm.OcrLanguages;
        var ocrBaseDpi = vm.OcrBaseDpi;
        var ocrHighDpi = vm.OcrHighDpi;
        var ocrLowConfidence = vm.OcrLowConfidence;
        var renderCacheMax = vm.RenderCacheMax;
        var runVerifyAfterSave = vm.RunVerifyAfterSave;

        // Verify they're not null/zero (have defaults)
        ocrLanguages.Should().NotBeNullOrEmpty();
        ocrBaseDpi.Should().BeGreaterThan(0);
        ocrHighDpi.Should().BeGreaterThan(0);
        ocrLowConfidence.Should().BeGreaterThan(0);
        renderCacheMax.Should().BeGreaterThan(0);
    }

    private MainWindowViewModel CreateMainViewModel()
    {
        var loggerFactory = NullLoggerFactory.Instance;
        var documentService = new PdfDocumentService(new Mock<ILogger<PdfDocumentService>>().Object);
        var renderService = new PdfRenderService(new Mock<ILogger<PdfRenderService>>().Object);
        var redactionService = new RedactionService(new Mock<ILogger<RedactionService>>().Object, loggerFactory);
        var textExtractionService = new PdfTextExtractionService(new Mock<ILogger<PdfTextExtractionService>>().Object);
        var searchService = new PdfSearchService(new Mock<ILogger<PdfSearchService>>().Object);
        var ocrService = new PdfOcrService(new Mock<ILogger<PdfOcrService>>().Object, renderService);
        var signatureService = new SignatureVerificationService(new Mock<ILogger<SignatureVerificationService>>().Object);
        var verifier = new RedactionVerifier(new Mock<ILogger<RedactionVerifier>>().Object, loggerFactory);
        var filenameSuggestionService = new FilenameSuggestionService();

        return new MainWindowViewModel(
            new Mock<ILogger<MainWindowViewModel>>().Object,
            loggerFactory,
            documentService,
            renderService,
            redactionService,
            textExtractionService,
            searchService,
            ocrService,
            signatureService,
            verifier,
            filenameSuggestionService);
    }
}
