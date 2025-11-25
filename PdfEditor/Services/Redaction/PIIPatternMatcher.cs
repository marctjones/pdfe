using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace PdfEditor.Services.Redaction;

/// <summary>
/// Pattern matcher for detecting Personally Identifiable Information (PII)
/// </summary>
public class PIIPatternMatcher
{
    private readonly ILogger<PIIPatternMatcher> _logger;

    /// <summary>
    /// Patterns for each PII type
    /// </summary>
    private static readonly Dictionary<PIIType, string[]> Patterns = new()
    {
        [PIIType.SSN] = new[]
        {
            @"\b\d{3}-\d{2}-\d{4}\b",           // 123-45-6789
            @"\b\d{3}\s\d{2}\s\d{4}\b",         // 123 45 6789
            @"\b\d{9}\b"                         // 123456789 (only if surrounded by non-digits)
        },
        [PIIType.Email] = new[]
        {
            @"\b[\w._%+-]+@[\w.-]+\.[A-Za-z]{2,}\b"
        },
        [PIIType.Phone] = new[]
        {
            @"\b\(?\d{3}\)?[-.\s]?\d{3}[-.\s]?\d{4}\b",      // (123) 456-7890, 123-456-7890
            @"\b\d{3}[-.\s]\d{4}\b",                          // 456-7890
            @"\b\+1[-.\s]?\(?\d{3}\)?[-.\s]?\d{3}[-.\s]?\d{4}\b"  // +1 (123) 456-7890
        },
        [PIIType.CreditCard] = new[]
        {
            @"\b\d{4}[-\s]?\d{4}[-\s]?\d{4}[-\s]?\d{4}\b",   // 1234-5678-9012-3456
            @"\b\d{16}\b"                                     // 1234567890123456
        },
        [PIIType.DateOfBirth] = new[]
        {
            @"\b\d{1,2}/\d{1,2}/\d{2,4}\b",     // MM/DD/YYYY or M/D/YY
            @"\b\d{4}-\d{2}-\d{2}\b",           // YYYY-MM-DD
            @"\b\d{1,2}-\d{1,2}-\d{2,4}\b"      // MM-DD-YYYY
        },
        [PIIType.Address] = new[]
        {
            @"\b\d+\s+[\w\s]+(?:Street|St|Avenue|Ave|Road|Rd|Boulevard|Blvd|Drive|Dr|Lane|Ln|Way|Court|Ct)\b",
            @"\b(?:P\.?O\.?\s*Box|PO\s*Box)\s+\d+\b"
        }
    };

    public PIIPatternMatcher(ILogger<PIIPatternMatcher> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Find all instances of a specific PII type in text
    /// </summary>
    public List<TextMatch> FindPII(string text, PIIType piiType)
    {
        var matches = new List<TextMatch>();

        if (string.IsNullOrEmpty(text) || !Patterns.ContainsKey(piiType))
            return matches;

        foreach (var pattern in Patterns[piiType])
        {
            try
            {
                var regex = new Regex(pattern, RegexOptions.IgnoreCase);
                var regexMatches = regex.Matches(text);

                foreach (Match match in regexMatches)
                {
                    var textMatch = new TextMatch
                    {
                        MatchedText = match.Value,
                        StartIndex = match.Index,
                        EndIndex = match.Index + match.Length,
                        Confidence = 1.0,
                        PIIType = piiType
                    };

                    // Validate the match
                    if (ValidateMatch(textMatch, piiType))
                    {
                        matches.Add(textMatch);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error matching pattern {Pattern} for {Type}", pattern, piiType);
            }
        }

        // Remove duplicates (same text at same position)
        return matches
            .GroupBy(m => (m.StartIndex, m.EndIndex))
            .Select(g => g.First())
            .ToList();
    }

    /// <summary>
    /// Find all PII of any type in text
    /// </summary>
    public List<TextMatch> FindAllPII(string text)
    {
        var allMatches = new List<TextMatch>();

        foreach (PIIType piiType in Enum.GetValues<PIIType>())
        {
            if (piiType == PIIType.Custom)
                continue;

            var matches = FindPII(text, piiType);
            allMatches.AddRange(matches);
        }

        // Sort by position and remove overlapping matches
        return allMatches
            .OrderBy(m => m.StartIndex)
            .ToList();
    }

    /// <summary>
    /// Validate a potential PII match
    /// </summary>
    public bool ValidateMatch(TextMatch match, PIIType piiType)
    {
        if (match == null || string.IsNullOrEmpty(match.MatchedText))
            return false;

        return piiType switch
        {
            PIIType.SSN => ValidateSSN(match.MatchedText),
            PIIType.CreditCard => ValidateCreditCard(match.MatchedText),
            PIIType.Email => ValidateEmail(match.MatchedText),
            PIIType.Phone => ValidatePhone(match.MatchedText),
            _ => true // Accept other types without additional validation
        };
    }

    /// <summary>
    /// Validate SSN format (basic validation, not checking against actual SSN database)
    /// </summary>
    private bool ValidateSSN(string ssn)
    {
        // Remove separators
        var digits = new string(ssn.Where(char.IsDigit).ToArray());

        if (digits.Length != 9)
            return false;

        // SSN cannot start with 000, 666, or 900-999
        var areaNumber = int.Parse(digits.Substring(0, 3));
        if (areaNumber == 0 || areaNumber == 666 || areaNumber >= 900)
            return false;

        // Group number cannot be 00
        var groupNumber = int.Parse(digits.Substring(3, 2));
        if (groupNumber == 0)
            return false;

        // Serial number cannot be 0000
        var serialNumber = int.Parse(digits.Substring(5, 4));
        if (serialNumber == 0)
            return false;

        return true;
    }

    /// <summary>
    /// Validate credit card number using Luhn algorithm
    /// </summary>
    private bool ValidateCreditCard(string cardNumber)
    {
        // Remove separators
        var digits = new string(cardNumber.Where(char.IsDigit).ToArray());

        if (digits.Length < 13 || digits.Length > 19)
            return false;

        // Luhn algorithm
        int sum = 0;
        bool alternate = false;

        for (int i = digits.Length - 1; i >= 0; i--)
        {
            int digit = digits[i] - '0';

            if (alternate)
            {
                digit *= 2;
                if (digit > 9)
                    digit -= 9;
            }

            sum += digit;
            alternate = !alternate;
        }

        return sum % 10 == 0;
    }

    /// <summary>
    /// Validate email format
    /// </summary>
    private bool ValidateEmail(string email)
    {
        try
        {
            var addr = new System.Net.Mail.MailAddress(email);
            return addr.Address == email;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Validate phone number (basic validation)
    /// </summary>
    private bool ValidatePhone(string phone)
    {
        var digits = new string(phone.Where(char.IsDigit).ToArray());

        // US phone numbers are 7, 10, or 11 digits
        return digits.Length == 7 || digits.Length == 10 || digits.Length == 11;
    }

    /// <summary>
    /// Get the pattern strings for a PII type
    /// </summary>
    public static string[] GetPatternsForType(PIIType piiType)
    {
        return Patterns.GetValueOrDefault(piiType, Array.Empty<string>());
    }

    /// <summary>
    /// Add a custom pattern for a PII type
    /// </summary>
    public void AddCustomPattern(PIIType piiType, string pattern)
    {
        if (!Patterns.ContainsKey(piiType))
        {
            Patterns[piiType] = new[] { pattern };
        }
        else
        {
            var existing = Patterns[piiType].ToList();
            existing.Add(pattern);
            Patterns[piiType] = existing.ToArray();
        }
    }
}
