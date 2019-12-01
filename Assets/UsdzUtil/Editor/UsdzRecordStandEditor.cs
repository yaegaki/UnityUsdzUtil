using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace UsdzUtil
{
    [CustomEditor(typeof(UsdzRecordStand))]
    public class UsdzRecordStandEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            var script = (UsdzRecordStand)target;
            GUILayout.Label("CurrentFrame");
            GUILayout.TextArea(script.CurrentFrame.ToString());

            var bg = GUI.backgroundColor;
            if (script.IsRecording)
            {
                GUI.backgroundColor = Color.white;
                if (GUILayout.Button("Stop"))
                {
                    script.Stop();
                    EditorApplication.isPaused = script.PauseWhenFinished;
                }
            }
            else
            {
                GUI.backgroundColor = Color.red;

                // プレイ中はアニメーションの録画
                if (EditorApplication.isPlaying)
                {
                    if (GUILayout.Button("Record"))
                    {
                        script.Record();
                    }
                }
                // 非プレイ中は1フレームのみのスナップショット
                else
                {
                    if (GUILayout.Button("Snapshot"))
                    {
                        if (script.Record())
                        {
                            // 即終了
                            script.Stop();
                        }
                    }
                }
            }

            GUI.backgroundColor = bg;


            GUI.backgroundColor = Color.red;
            if (GUILayout.Button("ExportFromFile"))
            {
                var filePath = EditorUtility.OpenFilePanel("Select usd file", "", "usd,usda,usdc");
                if (!string.IsNullOrEmpty(filePath))
                {
                    script.ExportUsdz(filePath, false);
                }
            }

            GUI.backgroundColor = bg;
        }
    }
}
