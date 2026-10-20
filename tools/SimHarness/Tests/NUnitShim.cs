// Minimal stand-in for the parts of NUnit the EditMode tests use. Only compiled by the headless
// harness; Unity uses the real NUnit from the Test Framework package.
using System;
using System.Collections.Generic;

namespace NUnit.Framework
{
    [AttributeUsage(AttributeTargets.Class)] public sealed class TestFixtureAttribute : Attribute { }
    [AttributeUsage(AttributeTargets.Method)] public sealed class TestAttribute : Attribute { }
    [AttributeUsage(AttributeTargets.Method)] public sealed class SetUpAttribute : Attribute { }

    public sealed class AssertionException : Exception
    {
        public AssertionException(string message) : base(message) { }
    }

    public static class Assert
    {
        private static void Fail(string what, string message)
            => throw new AssertionException(string.IsNullOrEmpty(message) ? what : $"{what} -- {message}");

        public static void IsTrue(bool condition, string message = null) { if (!condition) Fail("Expected true", message); }
        public static void IsFalse(bool condition, string message = null) { if (condition) Fail("Expected false", message); }
        public static void IsNull(object o, string message = null) { if (o != null) Fail("Expected null", message); }
        public static void IsNotNull(object o, string message = null) { if (o == null) Fail("Expected not null", message); }

        public static void AreEqual(object expected, object actual, string message = null)
        {
            // Like NUnit, compare numbers by value across types (int 77 == short 77).
            bool equal = IsNumeric(expected) && IsNumeric(actual)
                ? Convert.ToDecimal(expected) == Convert.ToDecimal(actual)
                : Equals(expected, actual);
            if (!equal) Fail($"Expected {expected} but was {actual}", message);
        }

        private static bool IsNumeric(object o) =>
            o is byte || o is sbyte || o is short || o is ushort || o is int || o is uint || o is long || o is ulong || o is float || o is double || o is decimal;

        public static void AreEqual(double expected, double actual, double delta, string message = null)
        {
            if (Math.Abs(expected - actual) > delta) Fail($"Expected {expected} +/- {delta} but was {actual}", message);
        }

        public static void AreNotEqual(object notExpected, object actual, string message = null)
        {
            if (Equals(notExpected, actual)) Fail($"Expected anything but {notExpected}", message);
        }

        private static int Cmp<T>(T a, T b) where T : IComparable<T> => a.CompareTo(b);

        public static void Greater<T>(T a, T b, string message = null) where T : IComparable<T> { if (Cmp(a, b) <= 0) Fail($"Expected {a} > {b}", message); }
        public static void GreaterOrEqual<T>(T a, T b, string message = null) where T : IComparable<T> { if (Cmp(a, b) < 0) Fail($"Expected {a} >= {b}", message); }
        public static void Less<T>(T a, T b, string message = null) where T : IComparable<T> { if (Cmp(a, b) >= 0) Fail($"Expected {a} < {b}", message); }
        public static void LessOrEqual<T>(T a, T b, string message = null) where T : IComparable<T> { if (Cmp(a, b) > 0) Fail($"Expected {a} <= {b}", message); }
    }
}
