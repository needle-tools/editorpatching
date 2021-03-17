using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Threading.Tasks;
using HarmonyLib;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace needle.EditorPatching
{
	internal struct EditorPatchProviderInfo
	{
		public string PatchID;
		public EditorPatchProvider Instance;
		public IReadOnlyList<EditorPatchData> Data;

		public override string ToString()
		{
			return PatchID + ", Patches: " + Data.Count;
		}
	}

	internal struct EditorPatchData
	{
		public EditorPatch Patch;
		public MethodInfo PrefixMethod;
		public MethodInfo PostfixMethod;
		public MethodInfo TranspilerMethod;
		public MethodInfo FinalizerMethod;
	}
	
	// TODO: expose HarmonyInstance.DEBUG = true -> https://github.com/pardeike/Harmony/issues/79#issuecomment-386356598

	public static class PatchManager
	{
		[MenuItem(Constants.MenuItem + "Clear Settings Cache", priority = -1000)]
		public static void ClearSettingsCache()
		{
			PatchManagerSettings.Clear(true);
		}


		[MenuItem(Constants.MenuItem + "Disable All Patches", priority = -1000)]
		public static void DisableAllPatches()
		{
			DisableAllPatches(true);
		}

		public static void DisableAllPatches(bool resetPersistence)
		{
			Debug.Log("DISABLE ALL PATCHES");
			foreach (var prov in patchProviders)
			{
				var instance = prov.Value.Instance;
				if (instance.GetIsActive())
				{
					DisablePatch(instance);
					if (resetPersistence) continue;
					// keep persistence state
					PatchManagerSettings.SetPersistentActive(instance.ID(), true);
				}
			}

			foreach (var man in KnownPatches)
			{
				if (man.IsActive)
				{
					man.DisablePatch();
					if (resetPersistence) continue;
					// keep persistence state
					PatchManagerSettings.SetPersistentActive(man.Id, true);
				}
			}
		}

		[InitializeOnLoadMethod]
		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
		private static void Init()
		{
			// PatchesCollector.CollectPatches();
			PatchesCollector.CollectAll();

			var patchType = typeof(EditorPatchProvider);
			var patches = TypeCache.GetTypesDerivedFrom(patchType);
			foreach (var patch in patches)
			{
				if (patch.IsAbstract) continue;
				if (patch.GetCustomAttribute<NoAutoDiscover>() != null) continue;
				RegisterPatch(FormatterServices.GetUninitializedObject(patch) as EditorPatchProvider);
			}

			DelayedEnablePatchedThatHadBeenActivePreviously();

			// AssemblyReloadEvents.beforeAssemblyReload += DisableAllPatches(false);
			// CompilationPipeline.compilationStarted += o => DisableAllPatches(false);
		}

		private static void DelayedEnablePatchedThatHadBeenActivePreviously()
		{
			if (!AutomaticallyEnablePersistentPatches) return;
			foreach (var patch in registeredPatchProviders)
			{
				if (PatchManagerSettings.PersistentActive(patch))
					EnablePatch(patch);
			}
		}

		private static readonly HashSet<EditorPatchProvider> registeredPatchProviders = new HashSet<EditorPatchProvider>();
		private static readonly Dictionary<string, EditorPatchProviderInfo> patchProviders = new Dictionary<string, EditorPatchProviderInfo>();
		private static readonly Dictionary<string, Harmony> harmonyPatches = new Dictionary<string, Harmony>();

		/// <summary>
		/// harmony instances (owners) that are not using patch providers
		/// </summary>
		private static readonly Dictionary<string, IManagedPatch> knownPatches = new Dictionary<string, IManagedPatch>();

		/// <summary>
		/// key is patch id, value is patch file path
		/// </summary>
		private static readonly Dictionary<string, string> filePaths = new Dictionary<string, string>();

		public static bool AutomaticallyEnablePersistentPatches = true;

		public static bool AllowDebugLogs
		{
			get => PatchManagerSettings.instance.DebugLog;
			set => PatchManagerSettings.instance.DebugLog = value;
		}

		public static IReadOnlyCollection<EditorPatchProvider> RegisteredPatchProviders => registeredPatchProviders;
		public static IReadOnlyCollection<IManagedPatch> KnownPatches => knownPatches.Values;

		public static bool IsActive(string id)
		{
			if (patchProviders.ContainsKey(id)) return patchProviders[id].Instance.GetIsActive();
			if (knownPatches.ContainsKey(id)) return knownPatches[id].IsActive;
			return false;
		}

		public static bool IsPersistentEnabled(string id) => PatchManagerSettings.PersistentActive(id);

		public static bool TryGetFilePathForPatch(EditorPatchProvider prov, out string path) => TryGetFilePathForPatch(prov?.ID(), out path);

		public static bool TryGetFilePathForPatch(string id, out string path)
		{
			if (string.IsNullOrEmpty(id))
			{
				path = null;
				return false;
			}

			if (filePaths.ContainsKey(id))
			{
				path = filePaths[id];
				return true;
			}

			path = null;
			return false;
		}

		public static bool PatchIsActive(EditorPatchProvider patchProvider)
		{
			return PatchIsActive(patchProvider.ID());
		}

		public static bool PatchIsActive(string id) => harmonyPatches.ContainsKey(id);

		public static bool IsRegistered(EditorPatchProvider provider)
		{
			if (provider == null) return false;
			var type = provider.GetType();
			return type.FullName != null && patchProviders.ContainsKey(type.FullName);
		}

		public static bool IsRegistered(string id) => knownPatches.ContainsKey(id);
        
		public static void UnregisterAndDisablePatch(EditorPatchProvider patchProvider)
		{
			var id = patchProvider?.ID();
			if (id == null || id.Length <= 0) return;
			if (registeredPatchProviders.Contains(patchProvider))
			{
				patchProvider.DisablePatch();
				registeredPatchProviders.Remove(patchProvider);
			}
			if (knownPatches.ContainsKey(id))
			{
				knownPatches[id].DisablePatch();
				knownPatches.Remove(id);
			}
		}

		public static void RegisterPatch(IManagedPatch patch)
		{
			var id = patch.Id;
			if (patchProviders.ContainsKey(id)) return;
			if (knownPatches.ContainsKey(id)) return;
			knownPatches.Add(id, patch);
			// Debug.Log(patch.Name + " -> " + PatchManagerSettings.PersistentActive(id)); 
			if (!PatchManagerSettings.PersistentActive(id)) patch.DisablePatch();
		}
        
		public static void RegisterPatch(EditorPatchProvider patchProvider, bool allowUpdate = false)
		{
			var id = patchProvider?.ID();

			// avoid registering patches multiple times
			if (id == null) return;
			if (!allowUpdate && patchProviders.ContainsKey(id)) return;
            
			if (!filePaths.ContainsKey(id))
			{
				var path = AssetDatabase.FindAssets(patchProvider.GetType().Name).Select(AssetDatabase.GUIDToAssetPath)
					.FirstOrDefault(f => f != null && f.EndsWith($".cs"));
				if (!string.IsNullOrEmpty(path))
				{
					filePaths.Add(id, path);
				}
			}
            
			if (string.IsNullOrEmpty(id))
			{
				// ReSharper disable once SuspiciousTypeConversion.Global
				Debug.LogWarning("A patch has no ID " + patchProvider + " and will be ignored");
				return;
			}

			var patches = patchProvider.GetPatches();
			var patchData = new List<EditorPatchData>();
			const BindingFlags flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
			foreach (var patch in patches)
			{
				var type = patch.GetType();
				// cache the static patch methods
				var prefix = type.GetMethod("Prefix", flags);
				var postfix = type.GetMethod("Postfix", flags);
				var transpiler = type.GetMethod("Transpiler", flags);
				var finalizer = type.GetMethod("Finalizer", flags);
				patchData.Add(new EditorPatchData()
				{
					Patch = patch,
					PrefixMethod = prefix,
					PostfixMethod = postfix,
					TranspilerMethod = transpiler,
					FinalizerMethod = finalizer
				});
			}

			// TBD: Do we want to automatically remove and possibly warn if methods are null?

			var info = new EditorPatchProviderInfo()
			{
				PatchID = id,
				Instance = patchProvider,
				Data = patchData
			};

			if (allowUpdate && patchProviders.ContainsKey(id))
			{
				var prev = patchProviders[id];
				registeredPatchProviders.RemoveWhere(e => e.Id == prev.PatchID);
				knownPatches.Remove(prev.PatchID);
				patchProviders[id] = info;
			}
			else 
				patchProviders.Add(id, info);
            
			registeredPatchProviders.Add(patchProvider);
			knownPatches.Add(id, patchProvider);
			patchProvider.OnRegistered();
		}

		public static Task EnablePatch(EditorPatchProvider patchProvider)
		{
			var patchType = patchProvider.ID();
			return EnablePatch(patchType);
		}

		public static Task EnablePatch(Type patchType)
		{
			var patchID = patchType.FullName;
			if (patchID == null) return Task.CompletedTask;
			return EnablePatch(patchID);
		}

		public static Task EnablePatch(string patchID)
		{
			SetHarmonyDebugState(AllowDebugLogs);
			
			var tasks = new List<Task>();
			if (patchProviders.ContainsKey(patchID) && !harmonyPatches.ContainsKey(patchID))
			{
				var info = patchProviders[patchID];
				var patchData = info.Data;
				if (patchData.Count <= 0)
				{
					if (AllowDebugLogs) Debug.LogWarning("Patch " + patchID + " did not return any methods");
				}

				if (patchProviders[patchID].Instance.OnWillEnablePatch())
				{
					var instance = new Harmony(patchID);
					var t = ApplyPatch(instance, info);
					tasks.Add(t);
					harmonyPatches.Add(patchID, instance);
					patchProviders[patchID].Instance.OnEnabledPatch();
					if(patchProviders[patchID].Instance.Persistent())
						PatchManagerSettings.SetPersistentActive(patchID, true);
					InternalEditorUtility.RepaintAllViews();
				}
			}
			else
			{
				if(AllowDebugLogs && !knownPatches.ContainsKey(patchID))
					Debug.LogWarning("Can not enable " + patchID + ": Patch is unknown");
			}

			if (knownPatches.ContainsKey(patchID))
			{
				var patch = knownPatches[patchID];
				if (!patch.IsActive)
				{
					patch.EnablePatch();
					PatchManagerSettings.SetPersistentActive(patchID, true);
					InternalEditorUtility.RepaintAllViews();
				}
			}

			return Task.WhenAll(tasks);
		}

		public static void DisablePatch(EditorPatchProvider patch)
		{
			if (patch == null) return;
			DisablePatch(patch.ID());
		}

		public static void DisablePatch(Type patch)
		{
			var patchID = patch.FullName;
			if (patchID == null) return;
			DisablePatch(patchID);
		}

		public static void DisablePatch(string patchID)
		{
			SetHarmonyDebugState(AllowDebugLogs);
			
			if (WaitingForActivation.Contains(patchID)) WaitingForActivation.Remove(patchID);

			if (patchProviders.ContainsKey(patchID) && harmonyPatches.ContainsKey(patchID))
			{
				var instance = harmonyPatches[patchID];
				instance.UnpatchAll(patchID);
				harmonyPatches.Remove(patchID);
				patchProviders[patchID].Instance.OnDisabledPatch();
				InternalEditorUtility.RepaintAllViews();
				PatchManagerSettings.SetPersistentActive(patchID, false);
			}
            
			if (knownPatches.ContainsKey(patchID))
			{
				var patch = knownPatches[patchID];
				if (!patch.IsActive) return;
				patch.DisablePatch();
				InternalEditorUtility.RepaintAllViews();
				PatchManagerSettings.SetPersistentActive(patchID, false);
			}
		}

		public static bool IsWaitingForLoad(string patchId) => WaitingForActivation.Contains(patchId);

		private static readonly HashSet<string> WaitingForActivation = new HashSet<string>();

		private static async Task ApplyPatch(Harmony instance, EditorPatchProviderInfo provider)
		{
			if (WaitingForActivation.Contains(provider.PatchID)) return;
			WaitingForActivation.Add(provider.PatchID);
			while (true)
			{
				if (!WaitingForActivation.Contains(provider.PatchID)) return;
				if (provider.Instance.AllPatchesAreReadyToLoad()) break;
				await Task.Delay(20);
			}
			
			// wait for harmony debug patch to finish before loading other patches
			var patchId = typeof(HarmonyUnityDebugLogPatch).FullName;
			while (AllowDebugLogs && provider.PatchID != patchId && IsPersistentEnabled(patchId) && IsWaitingForLoad(patchId))
			{
				await Task.Delay(1);
			}

			if (WaitingForActivation.Contains(provider.PatchID) == false) return;
			

			if (AllowDebugLogs)
				Debug.Log("APPLY PATCH: " + provider.PatchID);

			var patchData = provider.Data;
			foreach (var data in patchData)
			{
				var patch = data.Patch;
				// var allowLogs = AllowDebugLogs;
				try
				{
					await Task.Run(async () =>
					{
						foreach (var method in await patch.GetTargetMethods())
						{
							if (method == null) continue;
							if (!WaitingForActivation.Contains(provider.PatchID)) break;
							try
							{
								instance.Patch(
									method,
									data.PrefixMethod != null ? new HarmonyMethod(data.PrefixMethod) : null,
									data.PostfixMethod != null ? new HarmonyMethod(data.PostfixMethod) : null,
									data.TranspilerMethod != null ? new HarmonyMethod(data.TranspilerMethod) : null,
									data.FinalizerMethod != null ? new HarmonyMethod(data.FinalizerMethod) : null
								);
							}
							catch (NotSupportedException e)
							{
								Debug.LogWarning("Patching \"" + provider.Instance.Name + "\" is not supported: "+  e.Message +"\n" + e);
							}
							catch (Exception e)
							{
								Debug.LogWarning(provider.Instance.Name + " " + e.GetType() + ": " + e.Message + "\n\n" + method.Name + " (" +
								                 method.DeclaringType?.FullName + ")"
								                 + "\n\nFull Stacktrace:\n" + e.StackTrace);
							}
						}
					});

					if (!WaitingForActivation.Contains(provider.PatchID))
					{
						instance.UnpatchAll(provider.PatchID);
					}
				}
				catch (Exception e)
				{
					Debug.LogError("Patching failed " + provider.PatchID + "\n" + e);
					instance.UnpatchAll(provider.PatchID);
					break;
				}
			}

			if (WaitingForActivation.Contains(provider.PatchID))
				WaitingForActivation.Remove(provider.PatchID);
		}


		private static void SetHarmonyDebugState(bool state)
		{
			if (Harmony.DEBUG == state) return;
			Harmony.DEBUG = state;
			if (state)
			{
				var dir = Application.dataPath + "/../Logs";
				if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
				var fullPath = dir + "/harmony.log.txt";
				Environment.SetEnvironmentVariable("HARMONY_LOG_FILE", fullPath);
				Debug.Log("Set Harmony debug path to " + fullPath);
			}
		}

		
	}
}