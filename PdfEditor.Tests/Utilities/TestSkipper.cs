using System;
using Xunit;

namespace PdfEditor.Tests.Utilities
{
    /// <summary>
    /// A utility class to dynamically skip XUnit tests.
    /// This is useful when the decision to skip a test is made during
    /// the test's constructor or setup phase, based on runtime conditions.
    /// </summary>
    public static class TestSkipper
    {
        private static string? _skipReason = null;

        /// <summary>
        /// Sets the reason for skipping the current test.
        /// Once a reason is set, any subsequent call to ThrowIfSkipped() will
        /// throw a SkipException.
        /// </summary>
        /// <param name="reason">The reason why the test should be skipped.</param>
        public static void SkipTest(string reason)
        {
            if (string.IsNullOrWhiteSpace(reason))
            {
                throw new ArgumentException("Skip reason cannot be null or whitespace.", nameof(reason));
            }
            _skipReason = reason;
        }

        /// <summary>
        /// Throws a SkipException if a skip reason has been set.
        /// This method should be called early in the test method.
        /// </summary>
        /// <exception cref="Xunit.SkipException">Thrown if _skipReason is not null.</exception>
        public static void ThrowIfSkipped()
        {
            if (_skipReason != null)
            {
                var reason = _skipReason;
                // Reset the reason for the next test execution context
                _skipReason = null; 
                throw new SkipException(reason);
            }
        }

        /// <summary>
        /// Resets the skip reason. Should be called by test frameworks or collection fixtures
        /// if a static TestSkipper is used across multiple test instances.
        /// In this setup, it's called after a skip, but can be explicitly called for safety.
        /// </summary>
        public static void Reset()
        {
            _skipReason = null;
        }
    }
}
