using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PdfEditor.Services;
using PdfEditor.Services.Verification;
using PdfEditor.ViewModels;
using Xunit;

namespace PdfEditor.Tests.Unit;

/// <summary>
/// Unit tests for PreferencesViewModel
/// Tests preferences dialog logic, property bindings, and commands
/// </summary>
public class PreferencesViewModelTests
{
    [Fact]
    public void Constructor_SetsDefaultValues()
    {
        // Arrange & Act
        var vm = new PreferencesViewModel();

        // Assert
        vm.OcrLanguages.Should().Be("eng");
        vm.OcrBaseDpi.Should().Be(350);
        vm.OcrHighDpi.Should().Be(450);
        vm.OcrLowConfidence.Should().Be(0.6);
        vm.OcrPreprocess.Should().BeTrue();
        vm.OcrBinarize.Should().BeTrue();
        vm.OcrDenoiseRadius.Should().Be(0.8);
        vm.RenderCacheMax.Should().Be(20);
        vm.RunVerifyAfterSave.Should().BeTrue("verification after save should be enabled by default for security");
    }

    [Fact]
    public void ResetToDefaults_RestoresAllDefaultValues()
    {
        // Arrange
        var vm = new PreferencesViewModel
        {
            OcrLanguages = "fra+deu",
            OcrBaseDpi = 600,
            OcrHighDpi = 800,
            OcrLowConfidence = 0.9,
            OcrPreprocess = false,
            OcrBinarize = false,
            OcrDenoiseRadius = 2.5,
            RenderCacheMax = 100,
            RunVerifyAfterSave = false
        };

        // Act
        vm.ResetToDefaultsCommand.Execute().Subscribe();

        // Assert
        vm.OcrLanguages.Should().Be("eng");
        vm.OcrBaseDpi.Should().Be(350);
        vm.OcrHighDpi.Should().Be(450);
        vm.OcrLowConfidence.Should().Be(0.6);
        vm.OcrPreprocess.Should().BeTrue();
        vm.OcrBinarize.Should().BeTrue();
        vm.OcrDenoiseRadius.Should().Be(0.8);
        vm.RenderCacheMax.Should().Be(20);
        vm.RunVerifyAfterSave.Should().BeTrue();
    }

    [Fact]
    public void SaveCommand_SetsDialogResultToTrue()
    {
        // Arrange
        var vm = new PreferencesViewModel();
        vm.DialogResult.Should().BeFalse();

        // Act
        vm.SaveCommand.Execute().Subscribe();

        // Assert
        vm.DialogResult.Should().BeTrue();
    }

    [Fact]
    public void CancelCommand_SetsDialogResultToFalse()
    {
        // Arrange
        var vm = new PreferencesViewModel();

        // Act
        vm.CancelCommand.Execute().Subscribe();

        // Assert
        vm.DialogResult.Should().BeFalse();
    }

    [Fact]
    public void LoadFromMainViewModel_CopiesAllSettings()
    {
        // Arrange
        var mainVm = CreateMainViewModel();
        mainVm.OcrLanguages = "deu+fra";
        mainVm.OcrBaseDpi = 400;
        mainVm.OcrHighDpi = 500;
        mainVm.OcrLowConfidence = 0.7;
        mainVm.RenderCacheMax = 50;
        mainVm.RunVerifyAfterSave = false;

        var prefsVm = new PreferencesViewModel();

        // Act
        prefsVm.LoadFromMainViewModel(mainVm);

        // Assert
        prefsVm.OcrLanguages.Should().Be("deu+fra");
        prefsVm.OcrBaseDpi.Should().Be(400);
        prefsVm.OcrHighDpi.Should().Be(500);
        prefsVm.OcrLowConfidence.Should().Be(0.7);
        prefsVm.RenderCacheMax.Should().Be(50);
        prefsVm.RunVerifyAfterSave.Should().BeFalse();
    }

    [Fact]
    public void SaveToMainViewModel_CopiesAllSettings()
    {
        // Arrange
        var mainVm = CreateMainViewModel();
        var prefsVm = new PreferencesViewModel
        {
            OcrLanguages = "spa+ita",
            OcrBaseDpi = 300,
            OcrHighDpi = 600,
            OcrLowConfidence = 0.5,
            RenderCacheMax = 30,
            RunVerifyAfterSave = true
        };

        // Act
        prefsVm.SaveToMainViewModel(mainVm);

        // Assert
        mainVm.OcrLanguages.Should().Be("spa+ita");
        mainVm.OcrBaseDpi.Should().Be(300);
        mainVm.OcrHighDpi.Should().Be(600);
        mainVm.OcrLowConfidence.Should().Be(0.5);
        mainVm.RenderCacheMax.Should().Be(30);
        mainVm.RunVerifyAfterSave.Should().BeTrue();
    }

    [Fact]
    public void PropertyChanges_RaisePropertyChangedEvents()
    {
        // Arrange
        var vm = new PreferencesViewModel();
        var propertyChangedCount = 0;
        vm.PropertyChanged += (s, e) => propertyChangedCount++;

        // Act
        vm.OcrLanguages = "fra";
        vm.OcrBaseDpi = 400;
        vm.OcrHighDpi = 500;
        vm.OcrLowConfidence = 0.7;
        vm.OcrPreprocess = false;
        vm.OcrBinarize = false;
        vm.OcrDenoiseRadius = 1.5;
        vm.RenderCacheMax = 25;
        vm.RunVerifyAfterSave = false;

        // Assert
        propertyChangedCount.Should().Be(9, "all 9 properties should raise PropertyChanged");
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
