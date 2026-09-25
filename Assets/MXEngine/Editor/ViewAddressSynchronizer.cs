using System;
using System.Collections.Generic;
using MXEngine.MVP;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

namespace MXEngine.Editor
{
    [InitializeOnLoad]
    internal static class ViewAddressSynchronizer
    {
        private static bool _scheduled;
        private static bool _syncing;

        static ViewAddressSynchronizer()
        {
            EditorApplication.projectChanged += ScheduleSync;
            ScheduleSync();
        }

        [MenuItem("MXEngine/Sync View Addressable Keys")]
        private static void SyncFromMenu()
        {
            Sync(logResult: true);
        }

        private static void ScheduleSync()
        {
            if (_scheduled || _syncing)
                return;

            _scheduled = true;
            EditorApplication.delayCall += () =>
            {
                _scheduled = false;
                Sync(logResult: false);
            };
        }

        private static void Sync(bool logResult)
        {
            if (_syncing || EditorApplication.isCompiling || EditorApplication.isUpdating)
                return;

            var settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null)
                return;

            _syncing = true;
            try
            {
                var candidates = CollectCandidates(settings);
                var counts = CountDesiredAddresses(candidates);
                var changed = 0;

                foreach (var candidate in candidates)
                {
                    // More than one prefab using the same View type is intentionally ambiguous.
                    // Those entries keep their explicit addresses instead of being overwritten.
                    if (counts[candidate.DesiredAddress] != 1)
                        continue;

                    if (string.Equals(candidate.Entry.address, candidate.DesiredAddress,
                            StringComparison.Ordinal))
                        continue;

                    if (AddressUsedByAnotherEntry(settings, candidate.Entry, candidate.DesiredAddress))
                    {
                        Debug.LogWarning(
                            $"MXEngine skipped address '{candidate.DesiredAddress}' because another Addressable entry already uses it.");
                        continue;
                    }

                    candidate.Entry.SetAddress(candidate.DesiredAddress, false);
                    changed++;
                }

                if (changed > 0)
                {
                    EditorUtility.SetDirty(settings);
                    AssetDatabase.SaveAssets();
                }

                if (logResult)
                    Debug.Log(changed == 0
                        ? "MXEngine: View Addressable keys are already synchronized."
                        : $"MXEngine: synchronized {changed} View Addressable key(s).");
            }
            finally
            {
                _syncing = false;
            }
        }

        private static List<Candidate> CollectCandidates(AddressableAssetSettings settings)
        {
            var result = new List<Candidate>();
            foreach (var group in settings.groups)
            {
                if (group == null)
                    continue;

                foreach (var entry in group.entries)
                {
                    if (entry == null || string.IsNullOrEmpty(entry.AssetPath) ||
                        !entry.AssetPath.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
                        continue;

                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(entry.AssetPath);
                    if (prefab == null)
                        continue;

                    var viewType = FindViewType(prefab);
                    if (viewType == null)
                        continue;

                    result.Add(new Candidate(entry, viewType.Name));
                }
            }
            return result;
        }

        private static Type FindViewType(GameObject prefab)
        {
            Type found = null;
            foreach (var component in prefab.GetComponents<MonoBehaviour>())
            {
                if (component == null || !IsViewType(component.GetType()))
                    continue;

                if (found != null && found != component.GetType())
                {
                    Debug.LogWarning(
                        $"MXEngine skipped '{prefab.name}' because its root contains more than one View component type.",
                        prefab);
                    return null;
                }

                found = component.GetType();
            }
            return found;
        }

        private static bool IsViewType(Type type)
        {
            while (type != null && type != typeof(MonoBehaviour))
            {
                if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(View<>))
                    return true;
                type = type.BaseType;
            }
            return false;
        }

        private static Dictionary<string, int> CountDesiredAddresses(List<Candidate> candidates)
        {
            var counts = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var candidate in candidates)
            {
                counts.TryGetValue(candidate.DesiredAddress, out var count);
                counts[candidate.DesiredAddress] = count + 1;
            }
            return counts;
        }

        private static bool AddressUsedByAnotherEntry(
            AddressableAssetSettings settings,
            AddressableAssetEntry current,
            string address)
        {
            foreach (var group in settings.groups)
            {
                if (group == null)
                    continue;

                foreach (var entry in group.entries)
                {
                    if (entry == null || ReferenceEquals(entry, current))
                        continue;
                    if (string.Equals(entry.address, address, StringComparison.Ordinal))
                        return true;
                }
            }
            return false;
        }

        private readonly struct Candidate
        {
            internal readonly AddressableAssetEntry Entry;
            internal readonly string DesiredAddress;

            internal Candidate(AddressableAssetEntry entry, string desiredAddress)
            {
                Entry = entry;
                DesiredAddress = desiredAddress;
            }
        }
    }
}
