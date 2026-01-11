using Microsoft.Extensions.Logging;
using Pdfe.Core.Document;
using Pdfe.Core.Primitives;
using System;
using System.Collections.Generic;
using System.IO;
using Org.BouncyCastle.Cms;
using Org.BouncyCastle.X509;
using System.Linq;

namespace PdfEditor.Services;

public class SignatureVerificationResult
{
    public string SignatureName { get; set; } = string.Empty;
    public bool IsValid { get; set; }
    public string SignedBy { get; set; } = string.Empty;
    public DateTime SigningTime { get; set; }
    public string StatusMessage { get; set; } = string.Empty;
    public bool CoversWholeDocument { get; set; }
}

/// <summary>
/// Service for verifying digital signatures in PDF documents
/// Uses PdfSharp for parsing and BouncyCastle for cryptographic validation
/// </summary>
public class SignatureVerificationService
{
    private readonly ILogger<SignatureVerificationService> _logger;

    public SignatureVerificationService(ILogger<SignatureVerificationService> logger)
    {
        _logger = logger;
    }

    public List<SignatureVerificationResult> VerifySignatures(string pdfPath)
    {
        var results = new List<SignatureVerificationResult>();
        _logger.LogInformation("Verifying signatures for {File}", Path.GetFileName(pdfPath));

        try
        {
            // We use Pdfe.Core to open the document and find signature dictionaries
            using var document = PdfDocument.Open(pdfPath);

            // 1. Find the AcroForm dictionary
            var acroFormObj = document.Catalog.GetOptional("AcroForm");
            if (acroFormObj == null)
            {
                _logger.LogInformation("No AcroForm found, document has no signatures.");
                return results;
            }

            var acroForm = document.Resolve(acroFormObj) as PdfDictionary;
            if (acroForm == null)
            {
                _logger.LogInformation("AcroForm is not a dictionary.");
                return results;
            }

            // 2. Get Fields array
            var fieldsObj = acroForm.GetOptional("Fields");
            if (fieldsObj == null)
            {
                _logger.LogInformation("No fields found in AcroForm.");
                return results;
            }

            var fields = document.Resolve(fieldsObj) as PdfArray;
            if (fields == null)
            {
                _logger.LogInformation("Fields is not an array.");
                return results;
            }

            // 3. Iterate fields to find signatures
            foreach (var item in fields)
            {
                var fieldDict = document.Resolve(item) as PdfDictionary;
                if (fieldDict != null)
                {
                    CheckFieldForSignature(document, fieldDict, pdfPath, results);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error verifying signatures");
            results.Add(new SignatureVerificationResult 
            { 
                StatusMessage = $"Error: {ex.Message}", 
                IsValid = false 
            });
        }

        return results;
    }

    private void CheckFieldForSignature(PdfDocument document, PdfDictionary fieldDict, string pdfPath, List<SignatureVerificationResult> results)
    {
        // Check if it's a signature field (FT = Sig)
        var type = fieldDict.GetNameOrNull("FT");
        if (type != "Sig") return;

        var name = fieldDict.GetStringOrNull("T") ?? "Unknown";
        _logger.LogInformation("Found signature field: {Name}", name);

        // Get the signature value dictionary (V)
        var valueObj = fieldDict.GetOptional("V");
        if (valueObj == null)
        {
            _logger.LogWarning("Signature field {Name} has no value dictionary (unsigned)", name);
            return;
        }

        var valueDict = document.Resolve(valueObj) as PdfDictionary;
        if (valueDict == null)
        {
            _logger.LogWarning("Signature field {Name} value is not a dictionary", name);
            return;
        }

        var result = new SignatureVerificationResult { SignatureName = name };

        try
        {
            // 1. Get ByteRange
            var byteRangeObj = valueDict.GetOptional("ByteRange");
            var byteRangeArray = byteRangeObj != null ? document.Resolve(byteRangeObj) as PdfArray : null;
            if (byteRangeArray == null || byteRangeArray.Count != 4)
            {
                result.IsValid = false;
                result.StatusMessage = "Invalid or missing ByteRange";
                results.Add(result);
                return;
            }

            // 2. Get Contents (the PKCS#7 signature)
            var contents = valueDict.GetStringOrNull("Contents");
            if (string.IsNullOrEmpty(contents))
            {
                result.IsValid = false;
                result.StatusMessage = "Empty signature content";
                results.Add(result);
                return;
            }

            // Convert hex string to bytes
            // PdfSharp might return it as raw string or hex. 
            // Usually /Contents is a hex string in angle brackets <...>
            // We need to parse it.
            byte[] signatureBytes = ParseHexString(contents);

            // 3. Verify the signature using BouncyCastle
            VerifySignatureBytes(signatureBytes, result);

            // 4. Verify document integrity (hash check)
            // This requires reading the file bytes according to ByteRange and hashing them
            // Then comparing with the hash inside the signature
            // This is complex to implement fully correctly without a dedicated PDF crypto library,
            // but we can do a basic check if BouncyCastle supports detached signature verification.
            
            // For this MVP, we will focus on verifying the certificate chain and structure
            // Full byte-range verification requires re-hashing the file parts.
            
            // TODO: Implement full ByteRange extraction and hashing
            result.StatusMessage += " (Integrity check skipped in MVP)";

        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to verify signature {Name}", name);
            result.IsValid = false;
            result.StatusMessage = $"Verification failed: {ex.Message}";
        }

        results.Add(result);
    }

    private void VerifySignatureBytes(byte[] signatureBytes, SignatureVerificationResult result)
    {
        try
        {
            var cms = new CmsSignedData(signatureBytes);
            var store = cms.GetCertificates("Collection");
            var signers = cms.GetSignerInfos();

            foreach (SignerInformation signer in signers.GetSigners())
            {
                var certCollection = store.GetMatches(signer.SignerID);
                foreach (X509Certificate cert in certCollection)
                {
                    result.SignedBy = cert.SubjectDN.ToString();
                    result.SigningTime = DateTime.Now; // Placeholder, should extract from signer info if available
                    
                    // Verify signature
                    if (signer.Verify(cert))
                    {
                        result.IsValid = true;
                        result.StatusMessage = "Signature is cryptographically valid";
                    }
                    else
                    {
                        result.IsValid = false;
                        result.StatusMessage = "Signature verification failed";
                    }
                }
            }
        }
        catch (Exception ex)
        {
            throw new Exception("BouncyCastle verification failed", ex);
        }
    }

    private byte[] ParseHexString(string hex)
    {
        // Remove < > if present
        hex = hex.Trim('<', '>');
        // Remove whitespace
        hex = new string(hex.Where(c => !char.IsWhiteSpace(c)).ToArray());
        
        return Enumerable.Range(0, hex.Length)
                         .Where(x => x % 2 == 0)
                         .Select(x => Convert.ToByte(hex.Substring(x, 2), 16))
                         .ToArray();
    }
}
