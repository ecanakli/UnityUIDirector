using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;

namespace Ecanakli.UIDirector.EditorTools.Tests
{
    // The API reference page is written by hand. These tests compare it with the compiled assemblies by name, in both directions.
    [TestFixture]
    public sealed class ApiReferencePageTests
    {
        // The integration assemblies exist only when their library is installed; an absent one is left out.
        private static readonly string[] AssemblyNames =
        {
            "Ecanakli.UIDirector",
            "Ecanakli.UIDirector.Addressables",
            "Ecanakli.UIDirector.DOTween",
            "Ecanakli.UIDirector.DependencyInjection.Zenject",
        };

        private static readonly Regex SignatureBlock = new Regex(@"<!--\s*signature\s*-->\s*\n```csharp\n(.*?)\n```", RegexOptions.Singleline);

        // An attribute in square brackets, such as [Conditional("UNITY_EDITOR")], is not a member of the package.
        private static readonly Regex BracketedAttribute = new Regex(@"\[[^\[\]]*\]");

        // A capitalised name directly before an argument list, with an optional type argument list in between.
        private static readonly Regex CalledName = new Regex(@"\b([A-Z][A-Za-z0-9_]*)\s*(?:<[^<>()]*>)?\s*\(");

        [Test]
        public void Page_NamesEveryPublicTypeAndMember()
        {
            var page = ReadPage();
            var missing = new List<string>();
            foreach (var type in ExportedTypes())
            {
                var typeName = PlainName(type);
                if (!ContainsWord(page, typeName))
                {
                    missing.Add(type.FullName);
                    continue;
                }

                foreach (var member in PublicMemberNames(type))
                {
                    if (!ContainsWord(page, member))
                    {
                        missing.Add(typeName + "." + member);
                    }
                }
            }

            Assert.That(missing, Is.Empty, "public names that Documentation~/API-Reference.md does not mention");
        }

        // Every signature block of the package, not only those of the reference page: the README, llms.txt and each guide.
        [Test]
        public void SignatureBlocks_NameOnlyMembersThatExist()
        {
            var known = new HashSet<string>(StringComparer.Ordinal);
            var assemblies = new HashSet<string>(StringComparer.Ordinal);
            foreach (var type in ExportedTypes())
            {
                assemblies.Add(type.Assembly.GetName().Name);
                known.Add(PlainName(type));
                foreach (var member in PublicMemberNames(type))
                {
                    known.Add(member);
                }
            }

            // Without an integration's library installed, a member of that integration cannot be told from a name that does not exist.
            if (assemblies.Count < AssemblyNames.Length)
            {
                Assert.Pass("An integration assembly is not compiled in this project; the blocks are compared where all four are.");
            }

            var root = PackageRoot();
            var files = new List<string> { Path.Combine(root, "README.md"), Path.Combine(root, "llms.txt") };
            files.AddRange(Directory.GetFiles(Path.Combine(root, "Documentation~"), "*.md", SearchOption.AllDirectories));

            var blockCount = 0;
            var unknown = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var file in files)
            {
                var text = File.ReadAllText(file).Replace("\r\n", "\n");
                foreach (Match block in SignatureBlock.Matches(text))
                {
                    blockCount++;
                    foreach (Match call in CalledName.Matches(BracketedAttribute.Replace(block.Groups[1].Value, string.Empty)))
                    {
                        var name = call.Groups[1].Value;
                        if (!known.Contains(name))
                        {
                            unknown.Add(Path.GetFileName(file) + ": " + name);
                        }
                    }
                }
            }

            Assert.That(blockCount, Is.GreaterThan(30), "the signature blocks were found");
            Assert.That(unknown, Is.Empty, "names in signature blocks that no public type or member of the package has");
        }

        [Test]
        public void Assemblies_TheCoreAssemblyIsAlwaysCompared()
        {
            var names = new List<string>();
            foreach (var type in ExportedTypes())
            {
                names.Add(type.Assembly.GetName().Name);
            }

            Assert.That(names, Does.Contain("Ecanakli.UIDirector"));
            Assert.That(ContainsWord("a ViewRoot here", "ViewRoot"), Is.True);
            Assert.That(ContainsWord("ViewRootSetup", "ViewRoot"), Is.False, "a longer name does not count as the shorter one");
        }

        private static IEnumerable<Type> ExportedTypes()
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (Array.IndexOf(AssemblyNames, assembly.GetName().Name) < 0)
                {
                    continue;
                }

                foreach (var type in assembly.GetExportedTypes())
                {
                    if (!type.IsDefined(typeof(CompilerGeneratedAttribute), false))
                    {
                        yield return type;
                    }
                }
            }
        }

        // Declared public members a caller can name: no accessors, no operators, no overrides of System.Object.
        private static IEnumerable<string> PublicMemberNames(Type type)
        {
            if (typeof(Delegate).IsAssignableFrom(type))
            {
                yield break;
            }

            const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var member in type.GetMembers(flags))
            {
                if (member is ConstructorInfo || member is Type)
                {
                    continue;
                }

                if (member is MethodInfo method && (method.IsSpecialName || method.GetBaseDefinition().DeclaringType == typeof(object) || method.GetBaseDefinition().DeclaringType == typeof(ValueType)))
                {
                    continue;
                }

                if (member is FieldInfo field && field.IsSpecialName)
                {
                    continue;
                }

                // An explicit interface implementation is not public, so only plain names arrive here.
                if (seen.Add(member.Name))
                {
                    yield return member.Name;
                }
            }
        }

        private static string PlainName(Type type)
        {
            var name = type.Name;
            var tick = name.IndexOf('`');
            return tick < 0 ? name : name.Substring(0, tick);
        }

        private static bool ContainsWord(string text, string word)
        {
            return Regex.IsMatch(text, @"(?<![A-Za-z0-9_])" + Regex.Escape(word) + @"(?![A-Za-z0-9_])");
        }

        private static string ReadPage()
        {
            var path = Path.Combine(PackageRoot(), "Documentation~", "API-Reference.md");
            Assert.That(File.Exists(path), Is.True, "Documentation~/API-Reference.md exists");
            return File.ReadAllText(path).Replace("\r\n", "\n");
        }

        private static string PackageRoot()
        {
            var info = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(ViewRoot).Assembly);
            if (info != null && !string.IsNullOrEmpty(info.resolvedPath) && Directory.Exists(info.resolvedPath))
            {
                return info.resolvedPath;
            }

            return Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Packages", "com.ecanakli.uidirector"));
        }
    }
}
