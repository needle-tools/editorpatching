using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

namespace needle.EditorPatching
{
    public abstract class EditorPatchProvider : IManagedPatch
    {
        public virtual string DisplayName { get; }
        public virtual string Description { get; }
        public virtual bool Persistent() => true;

        public virtual string ID() => GetType().FullName;
        public string Name => DisplayName ?? GetType().Name;

        public string Id => ID();
        public bool IsActive => GetIsActive();
        
        public void EnablePatch()
        {
            this.Enable();
        }

        public void DisablePatch()
        {
            this.Disable();
        }
        
        private IReadOnlyList<EditorPatch> m_patches;
        public IReadOnlyList<EditorPatch> GetPatches()
        {
            if (m_patches != null && m_patches.Count > 0) return m_patches;
            var buff = new List<EditorPatch>();
            OnGetPatches(buff);
            m_patches = buff;
            return m_patches;
        }

        public bool AllPatchesAreReadyToLoad() => GetPatches().All(p => p.ReadyToLoad());

        protected abstract void OnGetPatches(List<EditorPatch> patches);

        public bool GetIsActive() => PatchManager.PatchIsActive(this);

        public Task Enable()
        {
            return PatchManager.EnablePatch(this);
        }

        public void Disable()
        {
            PatchManager.DisablePatch(this);
        }

        public virtual void OnRegistered()
        {
        }

        public virtual bool OnWillEnablePatch()
        {
            return true;
        }

        public virtual void OnEnabledPatch()
        {
        }

        public virtual void OnDisabledPatch()
        {
        }

        public virtual bool ActiveByDefault => false;
        
    }

    public abstract class EditorPatch
    {
        private IReadOnlyList<MethodBase> m_methods;

        public void ClearCache()
        {
            m_methods = null;
        }

        public virtual bool ReadyToLoad() => true;
        
        /// <summary>
        /// Collect methods to be patched.
        /// Implement the patches as used to according to Harmony Documentary as static methods:
        /// they NEED to be named Prefix and Postfix
        /// public static bool Prefix();
        /// public static void Postfix();
        /// </summary>
        public async Task<IReadOnlyList<MethodBase>> GetTargetMethods()
        {
            if (m_methods != null && m_methods.Count > 0) return m_methods;
            var list = new List<MethodBase>();
            await OnGetTargetMethods(list);
            m_methods = list;
            return m_methods;
        }

        protected abstract Task OnGetTargetMethods(List<MethodBase> targetMethods);
    }
}