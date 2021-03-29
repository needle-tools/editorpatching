using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace needle.EditorPatching
{
    public static class Utils
    {
        private static FieldInfo currentSkinField;
        
        public static bool GUISkinHasLoaded()
        {
            if (currentSkinField == null)
            {
                currentSkinField = typeof(GUISkin).GetField("current", BindingFlags.Static | BindingFlags.NonPublic);
                if (currentSkinField == null) return false;
            }
            var skin = (GUISkin) currentSkinField.GetValue(null);
            if (skin == null) return false;
            if (skin.name == "GameSkin") return false;
            return true;
        }
        
        public static async Task AwaitEditorReady()
        {
            while (EditorApplication.isCompiling || EditorApplication.isUpdating) await Task.Delay(5);
        }
    }
    
    
    public static class AssemblyExtensions
    {
        public static IEnumerable<Type> GetLoadableTypes(this Assembly assembly)
        {
            try
            {
                return assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException e)
            {
                return e.Types.Where(t => t != null);
            }
        }
    }


    public static class ExceptionExtensions
    {
        public static bool IsOrHasUnityException_CanOnlyBeCalledFromMainThread(this Exception e)
        {
            if (e == null) return false;
            if (e is UnityException un && un.Message.Contains("can only be called from the main thread."))
                return true;
            if (e.InnerException != null)
                return IsOrHasUnityException_CanOnlyBeCalledFromMainThread(e.InnerException);
            return false;
        }
    }
}