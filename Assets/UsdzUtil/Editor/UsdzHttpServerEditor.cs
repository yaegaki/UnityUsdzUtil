using UnityEditor;
using UnityEngine;

namespace UsdzUtil
{
    [CustomEditor(typeof(UsdzHttpServer))]
    public class UsdzHttpServerEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            var script = (UsdzHttpServer)target;

            var bg = GUI.backgroundColor;
            if (script.IsServing)
            {
                GUI.backgroundColor = Color.white;
                if (GUILayout.Button("Stop"))
                {
                    script.Stop();
                }
            }
            else
            {
                GUI.backgroundColor = Color.red;

                if (GUILayout.Button("Start"))
                {
                    script.StartServer();
                }
            }

            GUI.backgroundColor = bg;
        }
    }
}
