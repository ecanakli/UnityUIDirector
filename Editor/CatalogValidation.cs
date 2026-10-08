using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
#if ECANAKLI_UIDIRECTOR_ADDRESSABLES
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
#endif

namespace Ecanakli.UIDirector.EditorTools
{
    internal enum CatalogIssueSeverity
    {
        Error,
        Warning
    }

    // One finding: a catalog row (catalog and row index) or a key field (declaring type and field name).
    internal sealed class CatalogIssue
    {
        internal CatalogIssue(
            string id,
            CatalogIssueSeverity severity,
            string message,
            ViewCatalog catalog,
            int rowIndex,
            Type keyType,
            string keyField)
        {
            Id = id;
            Severity = severity;
            Message = message;
            Catalog = catalog;
            RowIndex = rowIndex;
            KeyType = keyType;
            KeyField = keyField;
        }

        internal string Id { get; }
        internal CatalogIssueSeverity Severity { get; }
        internal string Message { get; }

        // The catalog holding the row; null for a key with no row.
        internal ViewCatalog Catalog { get; }

        // The row index in Catalog; -1 when the issue is not tied to a row.
        internal int RowIndex { get; }

        // The class and field of the key; null for a row issue.
        internal Type KeyType { get; }
        internal string KeyField { get; }
    }

    internal readonly struct AddressLookupResult
    {
        internal AddressLookupResult(bool exists, UnityEngine.Object mainAsset)
        {
            Exists = exists;
            MainAsset = mainAsset;
        }

        internal bool Exists { get; }
        internal UnityEngine.Object MainAsset { get; }
    }

    internal sealed class CatalogValidationSettings
    {
        // The layers a row may name; null means the three default layers.
        internal IReadOnlyList<ViewLayer> KnownLayers { get; set; }

        internal bool AddressablesInstalled { get; set; }

        // Says whether an address exists and gives its main asset; null skips the address checks.
        internal Func<string, AddressLookupResult> LookupAddress { get; set; }
    }

    internal static class CatalogValidation
    {
        private const string MenuPath = "Tools/UIDirector/Validate Catalogs";
        private const string TmpDropdownTypeName = "TMPro.TMP_Dropdown";
        private const string NunitAssemblyName = "nunit.framework";

        private sealed class RowInfo
        {
            internal ViewCatalog Catalog;
            internal int Index;
            internal ViewEntry Entry;
            internal GameObject Prefab;
        }

        // Pure and deterministic: catalogs in the given order, rows in order, then keys by type name and field name.
        internal static List<CatalogIssue> Validate(
            IEnumerable<ViewCatalog> catalogs,
            IEnumerable<Type> keyTypes,
            CatalogValidationSettings settings)
        {
            var issues = new List<CatalogIssue>();
            settings = settings ?? new CatalogValidationSettings();

            var layers = new Dictionary<string, ViewLayer>(StringComparer.Ordinal);
            foreach (ViewLayer layer in settings.KnownLayers ?? DefaultLayers())
            {
                if (layer != null && !layers.ContainsKey(layer.Id))
                    layers.Add(layer.Id, layer);
            }

            var firstRowById = new Dictionary<string, RowInfo>(StringComparer.Ordinal);
            var refusedRowById = new Dictionary<string, RowInfo>(StringComparer.Ordinal);

            if (catalogs != null)
            {
                foreach (ViewCatalog catalog in catalogs)
                {
                    if (catalog == null)
                        continue;

                    IReadOnlyList<ViewEntry> entries = catalog.Entries;
                    for (int i = 0; i < entries.Count; i++)
                    {
                        if (entries[i] == null)
                            continue;

                        ValidateRow(issues, new RowInfo { Catalog = catalog, Index = i, Entry = entries[i] }, settings, layers, firstRowById, refusedRowById);
                    }
                }
            }

            if (keyTypes != null)
                ValidateKeys(issues, keyTypes, firstRowById, refusedRowById);

            return issues;
        }

        internal static List<ViewLayer> DefaultLayers()
        {
            return new List<ViewLayer> { ViewLayers.Screens, ViewLayers.Popups, ViewLayers.System };
        }

        // The defaults plus every static ViewLayer field in loaded assemblies that reference the core.
        internal static List<ViewLayer> FindKnownLayers()
        {
            List<ViewLayer> layers = DefaultLayers();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (ViewLayer layer in layers)
                seen.Add(layer.Id);

            Assembly core = typeof(ViewLayer).Assembly;
            string coreName = core.GetName().Name;

            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            Array.Sort(assemblies, (a, b) => string.CompareOrdinal(a.FullName, b.FullName));

            foreach (Assembly assembly in assemblies)
            {
                if (assembly.IsDynamic || !ReferencesCore(assembly, core, coreName))
                    continue;

                Type[] types;
                try
                {
                    types = assembly.GetTypes();
                }
                catch (ReflectionTypeLoadException exception)
                {
                    types = exception.Types;
                }
                catch (Exception)
                {
                    continue;
                }

                foreach (Type type in types)
                {
                    if (type == null || type.ContainsGenericParameters)
                        continue;

                    FieldInfo[] fields;
                    try
                    {
                        fields = type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly);
                    }
                    catch (Exception)
                    {
                        continue;
                    }

                    foreach (FieldInfo field in fields)
                    {
                        if (field.FieldType != typeof(ViewLayer))
                            continue;

                        try
                        {
                            if (field.GetValue(null) is ViewLayer layer && seen.Add(layer.Id))
                                layers.Add(layer);
                        }
                        catch (Exception)
                        {
                            // A failing static initializer hides its layers; the rows naming them report as unknown.
                        }
                    }
                }
            }

            return layers;
        }

        internal static CatalogValidationSettings DefaultSettings()
        {
            var settings = new CatalogValidationSettings { KnownLayers = FindKnownLayers() };
#if ECANAKLI_UIDIRECTOR_ADDRESSABLES
            settings.AddressablesInstalled = true;
            settings.LookupAddress = new AddressIndex().Lookup;
#endif
            return settings;
        }

        internal static List<ViewCatalog> FindProjectCatalogs()
        {
            string[] guids = AssetDatabase.FindAssets("t:ViewCatalog");
            var paths = new List<string>(guids.Length);
            foreach (string guid in guids)
                paths.Add(AssetDatabase.GUIDToAssetPath(guid));

            paths.Sort(StringComparer.Ordinal);

            var catalogs = new List<ViewCatalog>(paths.Count);
            foreach (string path in paths)
            {
                var catalog = AssetDatabase.LoadAssetAtPath<ViewCatalog>(path);
                if (catalog != null)
                    catalogs.Add(catalog);
            }

            return catalogs;
        }

        // Marked key classes of the project; classes in test assemblies are skipped, since fixtures have no catalog rows.
        internal static List<Type> FindKeyTypes()
        {
            return FindKeyTypes(false);
        }

        internal static List<Type> FindKeyTypes(bool includeTestAssemblies)
        {
            var types = new List<Type>();
            var testAssemblies = new Dictionary<Assembly, bool>();
            foreach (Type type in TypeCache.GetTypesWithAttribute<ViewKeysAttribute>())
            {
                if (!includeTestAssemblies && IsTestAssembly(type.Assembly, testAssemblies))
                    continue;

                types.Add(type);
            }

            return types;
        }

        private static bool IsTestAssembly(Assembly assembly, Dictionary<Assembly, bool> cache)
        {
            if (cache.TryGetValue(assembly, out bool known))
                return known;

            bool result = false;
            try
            {
                foreach (AssemblyName reference in assembly.GetReferencedAssemblies())
                {
                    if (reference.Name == NunitAssemblyName)
                    {
                        result = true;
                        break;
                    }
                }
            }
            catch (Exception)
            {
                result = false;
            }

            cache[assembly] = result;
            return result;
        }

        internal static List<CatalogIssue> ValidateProject()
        {
            return ValidateProject(out _);
        }

        private static List<CatalogIssue> ValidateProject(out int catalogCount)
        {
            List<ViewCatalog> catalogs = FindProjectCatalogs();
            catalogCount = catalogs.Count;
            return Validate(catalogs, FindKeyTypes(), DefaultSettings());
        }

        // Logs one line per issue with the id first, then a summary; returns the error count (warnings never fail a run).
        private static int Report(List<CatalogIssue> issues, int catalogCount)
        {
            int errors = 0;
            int warnings = 0;
            foreach (CatalogIssue issue in issues)
            {
                string line = issue.Id + ": " + issue.Message + " (see Troubleshooting#" + issue.Id.ToLowerInvariant() + ")";
                if (issue.Severity == CatalogIssueSeverity.Error)
                {
                    errors++;
                    Debug.LogError(line, issue.Catalog);
                }
                else
                {
                    warnings++;
                    Debug.LogWarning(line, issue.Catalog);
                }
            }

            string summary = "UIDirector catalog validation " + (errors > 0 ? "FAILED" : "PASSED") + ": " +
                errors + " error(s), " + warnings + " warning(s), " + catalogCount + " catalog(s).";
            Debug.Log(summary);
            return errors;
        }

        [MenuItem(MenuPath)]
        private static void ValidateCatalogsMenu()
        {
            List<CatalogIssue> issues = ValidateProject(out int catalogCount);
            Report(issues, catalogCount);
        }

        /// <summary>
        /// Batch-mode entry point for <c>-executeMethod</c>: validates every catalog and <c>[ViewKeys]</c> class in the project,
        /// logs like the menu (a line per issue with its id, then a PASSED or FAILED summary), and exits with code 1 when any
        /// error exists, else 0. Warnings, including <c>UIDIRECTOR124</c>, do not fail the run.
        /// </summary>
        public static void RunFromCommandLine()
        {
            int exitCode;
            try
            {
                List<CatalogIssue> issues = ValidateProject(out int catalogCount);
                exitCode = Report(issues, catalogCount) > 0 ? 1 : 0;
            }
            catch (Exception exception)
            {
                Debug.LogError("UIDirector catalog validation FAILED: the validator threw. " + exception);
                exitCode = 1;
            }

            EditorApplication.Exit(exitCode);
        }

        private static bool ReferencesCore(Assembly assembly, Assembly core, string coreName)
        {
            if (assembly == core)
                return true;

            try
            {
                foreach (AssemblyName reference in assembly.GetReferencedAssemblies())
                {
                    if (reference.Name == coreName)
                        return true;
                }
            }
            catch (Exception)
            {
                return false;
            }

            return false;
        }

        private static void ValidateRow(
            List<CatalogIssue> issues,
            RowInfo row,
            CatalogValidationSettings settings,
            Dictionary<string, ViewLayer> layers,
            Dictionary<string, RowInfo> firstRowById,
            Dictionary<string, RowInfo> refusedRowById)
        {
            ViewEntry entry = row.Entry;
            string label = RowLabel(row);

            // 125: a row without an id cannot be opened; no other rule applies to it.
            if (string.IsNullOrWhiteSpace(entry.Id))
            {
                AddRow(issues, DiagnosticIds.RowWithEmptyId, CatalogIssueSeverity.Error, row,
                    "Row " + row.Index + " in catalog '" + row.Catalog.name + "' has an empty id.");
                return;
            }

            // 109 before 108, as the runtime: a row on an unknown layer is refused before the duplicate check and claims no id.
            ViewLayer layer = null;
            bool layerKnown = !string.IsNullOrEmpty(entry.LayerId) && layers.TryGetValue(entry.LayerId, out layer);
            if (!layerKnown)
            {
                layer = null;
                AddRow(issues, DiagnosticIds.UnknownLayer, CatalogIssueSeverity.Error, row,
                    label + " names the unknown layer '" + entry.LayerId + "'.");
                refusedRowById.TryAdd(entry.Id, row);
            }
            else if (firstRowById.TryGetValue(entry.Id, out RowInfo first))
            {
                AddRow(issues, DiagnosticIds.DuplicateViewId, CatalogIssueSeverity.Error, row,
                    label + " repeats the id first used by " + RowLabel(first) + "; the second row is refused.");
            }
            else
            {
                firstRowById.Add(entry.Id, row);
            }

            // 121, 120, 124: where the prefab comes from.
            GameObject prefab = entry.Prefab;
            if (prefab == null)
            {
                if (string.IsNullOrEmpty(entry.AssetKey))
                {
                    AddRow(issues, DiagnosticIds.RowWithoutSource, CatalogIssueSeverity.Error, row,
                        label + " has neither a prefab nor an asset key.");
                }
                else if (!settings.AddressablesInstalled)
                {
                    AddRow(issues, DiagnosticIds.NoProviderForAssetKey, CatalogIssueSeverity.Warning, row,
                        label + " has the asset key '" + entry.AssetKey + "' but Addressables is not installed; it only loads when the root has a custom view provider.");
                }
                else if (settings.LookupAddress != null)
                {
                    AddressLookupResult result = settings.LookupAddress(entry.AssetKey);
                    if (!result.Exists)
                    {
                        // A warning: a custom view provider may use keys the validator cannot know.
                        AddRow(issues, DiagnosticIds.AssetKeyNotAddressable, CatalogIssueSeverity.Warning, row,
                            label + " has the asset key '" + entry.AssetKey + "', which matches no Addressables address, GUID or label. " +
                            "Ignore this warning when the root uses another asset provider.");
                    }
                    else
                    {
                        prefab = result.MainAsset as GameObject;
                    }
                }
            }

            row.Prefab = prefab;

            if (prefab != null)
            {
                // 103: the prefab root needs a ViewHost.
                if (prefab.GetComponent<ViewHost>() == null)
                {
                    AddRow(issues, DiagnosticIds.MissingViewHost, CatalogIssueSeverity.Error, row,
                        label + " uses the prefab '" + prefab.name + "', whose root has no ViewHost.");
                }

                // 123: a dropdown keeps its list above the layers and the shield.
                if (ContainsDropdown(prefab))
                {
                    AddRow(issues, DiagnosticIds.DropdownInView, CatalogIssueSeverity.Warning, row,
                        label + " uses the prefab '" + prefab.name + "', which contains a dropdown; its open list stays above the layers and the input shield.");
                }
            }

            // 122: Close on a layer that does not take back.
            if (entry.Back == BackPolicy.Close && layer != null && !layer.Has(ViewLayerRules.TakesBack))
            {
                AddRow(issues, DiagnosticIds.BackCloseOnLayerWithoutBack, CatalogIssueSeverity.Warning, row,
                    label + " sets BackPolicy.Close on the layer '" + layer.Id + "', which does not take back.");
            }
        }

        private static void ValidateKeys(
            List<CatalogIssue> issues,
            IEnumerable<Type> keyTypes,
            Dictionary<string, RowInfo> firstRowById,
            Dictionary<string, RowInfo> refusedRowById)
        {
            var types = new List<Type>();
            foreach (Type type in keyTypes)
            {
                if (type != null && !type.ContainsGenericParameters && !types.Contains(type))
                    types.Add(type);
            }

            types.Sort((a, b) => string.CompareOrdinal(a.FullName, b.FullName));

            foreach (Type type in types)
            {
                // Every static key field, whatever its visibility; game key classes are often internal.
                FieldInfo[] fields = type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly);
                Array.Sort(fields, (a, b) => string.CompareOrdinal(a.Name, b.Name));

                foreach (FieldInfo field in fields)
                {
                    Type fieldType = field.FieldType;
                    if (!fieldType.IsGenericType || fieldType.GetGenericTypeDefinition() != typeof(ViewKey<,>))
                        continue;

                    string id = ReadKeyId(field);
                    if (string.IsNullOrWhiteSpace(id))
                        continue;

                    ValidateKey(issues, type, field, id, firstRowById, refusedRowById);
                }
            }
        }

        private static void ValidateKey(
            List<CatalogIssue> issues,
            Type type,
            FieldInfo field,
            string id,
            Dictionary<string, RowInfo> firstRowById,
            Dictionary<string, RowInfo> refusedRowById)
        {
            string label = "Key '" + type.FullName + "." + field.Name + "' (id '" + id + "')";

            // 101: a key with no registered row.
            if (!firstRowById.TryGetValue(id, out RowInfo row))
            {
                if (refusedRowById.TryGetValue(id, out RowInfo refused))
                {
                    issues.Add(new CatalogIssue(DiagnosticIds.UnknownViewId, CatalogIssueSeverity.Error,
                        label + " has no catalog row the root registers; " + RowLabel(refused) + " is refused because it names an unknown layer.",
                        refused.Catalog, refused.Index, type, field.Name));
                }
                else
                {
                    issues.Add(new CatalogIssue(DiagnosticIds.UnknownViewId, CatalogIssueSeverity.Error,
                        label + " has no catalog row.", null, -1, type, field.Name));
                }

                return;
            }

            // 102: the prefab root needs IView<TArgs, TResult> for the key's types.
            if (row.Prefab == null)
                return;

            Type[] arguments = field.FieldType.GetGenericArguments();
            Type viewInterface = typeof(IView<,>).MakeGenericType(arguments);
            if (!HasComponentOfInterface(row.Prefab, viewInterface))
            {
                issues.Add(new CatalogIssue(DiagnosticIds.ViewTypeMismatch, CatalogIssueSeverity.Error,
                    label + ": the prefab '" + row.Prefab.name + "' of " + RowLabel(row) + " has no component on its root implementing IView<" +
                    arguments[0].Name + ", " + arguments[1].Name + ">.",
                    row.Catalog, row.Index, type, field.Name));
            }
        }

        private static string ReadKeyId(FieldInfo field)
        {
            try
            {
                object key = field.GetValue(null);
                PropertyInfo property = field.FieldType.GetProperty("Id", BindingFlags.Public | BindingFlags.Instance);
                return key == null || property == null ? null : property.GetValue(key) as string;
            }
            catch (Exception)
            {
                // A key whose static initializer fails has no readable id.
                return null;
            }
        }

        private static bool HasComponentOfInterface(GameObject prefab, Type viewInterface)
        {
            foreach (Component component in prefab.GetComponents<Component>())
            {
                if (component != null && viewInterface.IsInstanceOfType(component))
                    return true;
            }

            return false;
        }

        private static bool ContainsDropdown(GameObject prefab)
        {
            foreach (Component component in prefab.GetComponentsInChildren<Component>(true))
            {
                // A missing script reads as null.
                if (component == null)
                    continue;

                if (component is UnityEngine.UI.Dropdown)
                    return true;

                // TMP is not referenced, so match by full name through the base chain.
                for (Type type = component.GetType(); type != null; type = type.BaseType)
                {
                    if (type.FullName == TmpDropdownTypeName)
                        return true;
                }
            }

            return false;
        }

        private static string RowLabel(RowInfo row)
        {
            return "Row " + row.Index + " ('" + row.Entry.Id + "') in catalog '" + row.Catalog.name + "'";
        }

        private static void AddRow(List<CatalogIssue> issues, string id, CatalogIssueSeverity severity, RowInfo row, string message)
        {
            issues.Add(new CatalogIssue(id, severity, message, row.Catalog, row.Index, null, null));
        }

#if ECANAKLI_UIDIRECTOR_ADDRESSABLES
        // Key to entry map built on first use; one validation run reads the settings once.
        // A key is an address, a GUID or a label, like the Addressables view provider accepts.
        private sealed class AddressIndex
        {
            private Dictionary<string, AddressableAssetEntry> _entries;

            internal AddressLookupResult Lookup(string key)
            {
                if (_entries == null)
                    Build();

                if (!_entries.TryGetValue(key, out AddressableAssetEntry entry))
                    return default;

                // A label names a group of assets, so there is no single main asset to check; its entry is null.
                return new AddressLookupResult(true, entry != null ? entry.MainAsset : null);
            }

            private void Build()
            {
                _entries = new Dictionary<string, AddressableAssetEntry>(StringComparer.Ordinal);

                AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
                if (settings == null)
                    return;

                var all = new List<AddressableAssetEntry>();
                settings.GetAllAssets(all, false);
                // Addresses first, then GUIDs, then labels, so a direct match wins over a label.
                foreach (AddressableAssetEntry entry in all)
                {
                    if (entry != null && !string.IsNullOrEmpty(entry.address))
                        _entries.TryAdd(entry.address, entry);
                }

                foreach (AddressableAssetEntry entry in all)
                {
                    if (entry != null && !string.IsNullOrEmpty(entry.guid))
                        _entries.TryAdd(entry.guid, entry);
                }

                foreach (AddressableAssetEntry entry in all)
                {
                    if (entry == null || entry.labels == null)
                        continue;

                    foreach (string label in entry.labels)
                    {
                        if (!string.IsNullOrEmpty(label))
                            _entries.TryAdd(label, null);
                    }
                }
            }
        }
#endif
    }
}
