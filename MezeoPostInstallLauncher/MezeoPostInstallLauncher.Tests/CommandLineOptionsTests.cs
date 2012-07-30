using System;
using System.Text;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace MezeoPostInstallLauncher.Tests
{
    [TestClass]
    public class CommandLineOptionsTests 
    {
        [TestMethod]
        public void NullTest()
        {
            var result = CommandLineOptions.ParseArgsToDictionary(null, null);
            Assert.IsNotNull(result, "ParseArgsToDictionary result is null");
            Assert.AreEqual(0, result.Count());
        }

        [TestMethod]
        public void EmptyTest()
        {
            var result = CommandLineOptions.ParseArgsToDictionary(new string[] { }, null);
            Assert.IsNotNull(result, "ParseArgsToDictionary result is null");
            Assert.AreEqual(0, result.Count());
        }

        [TestMethod]
        public void SingleNamedArgumentTest()
        {
            var result = CommandLineOptions.ParseArgsToDictionary(new string[] { "/Key1=Value1" }, null);
            Assert.IsNotNull(result, "ParseArgsToDictionary result is null");
            Assert.AreEqual(1, result.Count());
            Assert.AreEqual("Value1", result["Key1"]);
        }

        [TestMethod]
        public void SingleUnescapedNamedArgumentTest()
        {
            var result = CommandLineOptions.ParseArgsToDictionary(new string[] { "Key1=Value1" }, null, CommandLineOptions.ParseArgsSettings.None);
            Assert.IsNotNull(result, "ParseArgsToDictionary result is null");
            Assert.AreEqual(1, result.Count());
            Assert.AreEqual("Value1", result["Key1"]);
        }

        [TestMethod]
        public void SingleNamedBooleanArgumentTrueTest()
        {
            var result = CommandLineOptions.ParseArgsToDictionary(new string[] { "/Key1" }, null);
            Assert.IsNotNull(result, "ParseArgsToDictionary result is null");
            Assert.AreEqual(1, result.Count());
            Assert.AreEqual(Boolean.TrueString, result["Key1"]);
        }

        [TestMethod]
        public void SingleNamedBooleanArgumentFalseTest()
        {
            var result = CommandLineOptions.ParseArgsToDictionary(new string[] { "/Key1=False" }, null);
            Assert.IsNotNull(result, "ParseArgsToDictionary result is null");
            Assert.AreEqual(1, result.Count());
            Assert.AreEqual(Boolean.FalseString, result["Key1"]);
        }

        [TestMethod]
        public void SingleOrderedArgumentTest()
        {
            var result = CommandLineOptions.ParseArgsToDictionary(new string[] { "Value1" }, new string[] { "Key1" });
            Assert.IsNotNull(result, "ParseArgsToDictionary result is null");
            Assert.AreEqual(1, result.Count());
            Assert.AreEqual("Value1", result["Key1"]);
        }

        [TestMethod]
        public void TooManyOrderedKeys()
        {
            var result = CommandLineOptions.ParseArgsToDictionary(new string[] { "Value1" }, new string[] { "Key1", "Key2", "Key3" });
            Assert.IsNotNull(result, "ParseArgsToDictionary result is null");
            Assert.AreEqual(1, result.Count());
            Assert.AreEqual("Value1", result["Key1"]);
        }

        [TestMethod]
        public void TooManyOrderedArguments()
        {
            var result = CommandLineOptions.ParseArgsToDictionary(new string[] { "Value1", "Value2", "Value3" }, new string[] { "Key1" });
            Assert.IsNotNull(result, "ParseArgsToDictionary result is null");
            Assert.AreEqual(1, result.Count());
            Assert.AreEqual("Value1", result["Key1"]);
        }

        [TestMethod]
        public void MixedCommandLine()
        {
            var result = CommandLineOptions.ParseArgsToDictionary(new string[] { "/File=C:\\Abc.txt", "Bob.Smith", "/Debug" }, new string[] { "UserName" });
            Assert.IsNotNull(result, "ParseArgsToDictionary result is null");
            Assert.AreEqual(3, result.Count());
            Assert.AreEqual("C:\\Abc.txt", result["file"]);
            Assert.AreEqual("Bob.Smith", result["username"]);
            Assert.AreEqual(Boolean.TrueString, result["debug"]);
        }

        [TestMethod]
        public void DuplicateArguments()
        {
            // Demonstrates that named-arguments take precedence over ordered-arguments, regardless of the order
            {
                var result = CommandLineOptions.ParseArgsToDictionary(new string[] { "/Key1=Value1", "Value2" }, new string[] { "Key1" });
                Assert.IsNotNull(result, "ParseArgsToDictionary result is null");
                Assert.AreEqual(1, result.Count());
                Assert.AreEqual("Value1", result["Key1"]);
            }
            {
                var result = CommandLineOptions.ParseArgsToDictionary(new string[] { "Value2", "/Key1=Value1" }, new string[] { "Key1" });
                Assert.IsNotNull(result, "ParseArgsToDictionary result is null");
                Assert.AreEqual(1, result.Count());
                Assert.AreEqual("Value1", result["Key1"]);
            }
        }

        [TestMethod]
        public void MixedArgumentsWithDuplicateNamedAndOrdered()
        {
            // Demonstrates that the two ordered arguments are assigned to Key2 and Key3 since Key1 has already been supplied via a named argument
            var result = CommandLineOptions.ParseArgsToDictionary(new string[] { "/Key1=Value1", "Value2", "Value3" }, new string[] { "Key1", "Key2", "Key3" });
            Assert.IsNotNull(result, "ParseArgsToDictionary result is null");
            Assert.AreEqual(3, result.Count());
            Assert.AreEqual("Value1", result["Key1"]);
            Assert.AreEqual("Value2", result["Key2"]);
            Assert.AreEqual("Value3", result["Key3"]);
        }
    }
}
