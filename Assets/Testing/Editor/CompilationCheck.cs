using UnityEngine;

namespace UIControl.Testing.Editor
{
    public static class CompilationCheck
    {
        // An executeMethod marker proves scripts compiled and the method ran.
        public static void Run() => Debug.Log("UICONTROL_COMPILATION_OK");
    }
}
