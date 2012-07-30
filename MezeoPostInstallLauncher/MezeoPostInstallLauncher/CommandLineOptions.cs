using System;
using System.Collections.Generic;
using System.Linq;

namespace MezeoPostInstallLauncher
{
    public class CommandLineOptions : IEnumerable<KeyValuePair<string,string>>
    {
        /// <summary>
        /// Initializes a new instance of the CommandLineOptions class.
        /// </summary>
        public CommandLineOptions(
            IEnumerable<string> args,
            IEnumerable<string> orderedArgumentNames = null,
            ParseArgsSettings settings = ParseArgsSettings.EscapeKeyValueArgs)
        {
            Options = ParseArgsToDictionary(args, orderedArgumentNames, settings);
        }

        public Dictionary<string, string> Options { get; set; }

        public static CommandLineOptions Parse(
            IEnumerable<string> args,
            IEnumerable<string> orderedArgumentNames = null,
            ParseArgsSettings settings = ParseArgsSettings.EscapeKeyValueArgs)
        {
            return new CommandLineOptions(args, orderedArgumentNames, settings);
        }

        [Flags]
        public enum ParseArgsSettings
        {
            None = 0x00,
            EscapeKeyValueArgs = 0x01
        }

        /// <summary>
        /// Parses a list of input command-line parameters into a dictionary of key/value pairs.
        /// This method supports parsing named arguments specified in "/key=value" form and also ordered arguments in "value" form.
        /// When parsing ordered arguments, the caller must specify the list of arguments names to be used when matching ordered argument values.
        /// </summary>
        /// <param name="args"></param>
        /// <param name="orderedArgumentNames">List of argument names used when interpreting ordered arguments.</param>
        /// <returns>A dictionary of parsed arguments</returns>
        public static Dictionary<string, string> ParseArgsToDictionary(IEnumerable<string> args, IEnumerable<string> orderedArgumentNames = null, ParseArgsSettings settings = ParseArgsSettings.EscapeKeyValueArgs)
        {
            if (orderedArgumentNames == null)
                orderedArgumentNames = new string[] { };

            if (args == null)
                args = new string[] { };

            // Trim any excess whitespace around the argument
            args = args.Select(arg => arg.Trim());

            var namedArguments = args
                                    .Where(arg =>
                                        {
                                            if (((int)(object)settings & (int)(object)ParseArgsSettings.EscapeKeyValueArgs) == (int)(object)ParseArgsSettings.EscapeKeyValueArgs)
                                                return arg.StartsWith("-") || arg.StartsWith("/");
                                            else
                                                return arg.Contains('=') || arg.Contains(':');
                                        });
            var orderedArguments = args
                                    .Except(namedArguments);

            var parsedNamedArguments = namedArguments
                // Split each key=value argument into a string array
                            .Select(strArg => strArg.Split("=:".ToCharArray(), 2, StringSplitOptions.RemoveEmptyEntries))

                            // Turn string array into a Tuple<string,string>
                            .Select(argPair =>
                            {
                                string key = argPair[0].TrimStart("/-".ToCharArray());
                                if (argPair.Count() == 1)
                                    return new Tuple<string, string>(key, Boolean.TrueString);
                                return new Tuple<string, string>(key, argPair[1]);
                            });

            // Remove any arguments from the expected ordered argument list that have already been specified as named-arguments
            orderedArgumentNames = orderedArgumentNames
                            .Except(parsedNamedArguments
                                .Select(arg => arg.Item1),
                                StringComparer.InvariantCultureIgnoreCase);

            // Turned ordered arguments into key/value tuples
            var parsedOrderedArguments = orderedArgumentNames
                // Combine ordered argument values with matching ordered argument names (keys)
                            .Zip(orderedArguments, (key, value) => new Tuple<string, string>(key, value));

            var result = parsedNamedArguments
                // Filter by unique keys (in case of duplicate command line options)
                            .Union(parsedOrderedArguments, InlineEqualityComparer<Tuple<string, string>>.Create((key1, key2) => key1.Item1.Equals(key2.Item1, StringComparison.InvariantCultureIgnoreCase), key => key.Item1.GetHashCode()))

                            // Turn into a dictionary
                            .ToDictionary(a1 => a1.Item1, a2 => a2.Item2, StringComparer.InvariantCultureIgnoreCase);

            return result;
        }

        public string this[string key]
        {
            get
            {
                string result;
                if (Options.TryGetValue(key, out result))
                    return result;
                return "";
            }
            set { Options[key] = value; }
        }

        public IEnumerable<string> Keys
        {
            get { return Options.Keys; }
        }

        public void Set(string key, string value)
        {
            Options[key] = value;
        }

        public string Get(string key)
        {
            return Options[key];
        }

        public bool TryGet(string key, out string value)
        {
            return Options.TryGetValue(key, out value);
        }

        public IEnumerator<KeyValuePair<string, string>> GetEnumerator()
        {
            return Options.GetEnumerator();
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }

}
