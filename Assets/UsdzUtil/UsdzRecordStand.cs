using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using pxr;
using Unity.Formats.USD;
using UnityEngine;
using USD.NET;

namespace UsdzUtil
{
    [DefaultExecutionOrder(9999)]
    public class UsdzRecordStand : MonoBehaviour
    {
        [SerializeField]
        private string exportDirectory = "usdz";

        [SerializeField]
        private string exportFileName = default;

        [SerializeField]
        private bool createUsdaFile = default;

        [SerializeField]
        private Camera thumbnailCamera = default;

        [SerializeField]
        private Transform exportRoot = default;

        [Serializable]
        public enum FrameRate
        {
            Fps6 = 6,
            Fps12 = 12,
            Fps15 = 15,
            Fps24 = 24,
            Fps30 = 30,
            Fps48 = 48,
            Fps60 = 60,
            Fps72 = 72,
            Fps75 = 75,
            Fps90 = 90,
        }

        /// <summary>
        /// usdのフレームレート。
        /// Unityのフレームレートとは違う値を設定することができる。
        /// 高くすればなめらかなアニメーションになるがファイルサイズが増える。
        /// Unityのフレームレート以上の値を設定した場合、結果がおかしくなる。
        /// </summary>
        [SerializeField]
        private FrameRate frameRate = FrameRate.Fps24;

        [SerializeField]
        private float recordSec = 5f; 

        [SerializeField]
        private bool flipZ = true;

        [SerializeField]
        private bool pauseWhenFinished = true;

        public bool IsRecording { get; private set; }
        public int CurrentFrame => currentFrame;
        public bool PauseWhenFinished => pauseWhenFinished;

        private Scene usdScene;
        private ExportContext exportContext;

        private bool isFirstFrame;
        private float timeUnit;
        // 前フレームからの経過時間
        private float elapsedTime;
        private int currentFrame;

        public bool Record()
        {
            if (this.IsRecording) return false;

            var recordTargets = this.exportRoot
                .OfType<Transform>()
                .ToArray();

            if (recordTargets.Length == 0)
            {
                Debug.LogError("出力対象がありません");
                return false;
            }

            var filePath = GetUsdFilePath(recordTargets);
            if (string.IsNullOrEmpty(filePath))
            {
                Debug.LogError("出力ファイル名を設定してください");
                return false;
            }

            CaptureThumbnail(Path.GetFileNameWithoutExtension(filePath));

            this.usdScene = Scene.Create(filePath);

            this.usdScene.FrameRate = (double)this.frameRate;
            this.usdScene.Time = null;
            this.usdScene.StartTime = 0;
            this.usdScene.EndTime = Math.Floor(this.usdScene.FrameRate * this.recordSec);

            this.exportContext = new ExportContext();
            this.exportContext.scene = this.usdScene;
            this.exportContext.basisTransform = BasisTransformation.SlowAndSafe;
            this.exportContext.exportMaterials = true;
            this.exportContext.activePolicy = ActiveExportPolicy.ExportAsVisibility;

            this.isFirstFrame = true;
            this.timeUnit = 1f / (float)this.frameRate;
            this.elapsedTime = 0f;
            this.currentFrame = 0;

            this.IsRecording = true;

            return true;
        }

        public void Stop()
        {
            if (!this.IsRecording) return;

            try
            {
                // まだ1フレームも記録していない
                // アニメーションしないusdとして出力する
                if (this.isFirstFrame)
                {
                    this.isFirstFrame = false;
                    SceneExporter.SyncExportContext(this.exportRoot.gameObject, this.exportContext);
                    ExportScene();
                }

                this.usdScene.Save();
                this.usdScene.Close();

                var usdFilePath = GetUsdFilePath();
                if (!File.Exists(usdFilePath))
                {
                    Debug.LogError($"ファイルが出力されていません: '{usdFilePath}'");
                    return;
                }

                var fullPath = Path.GetFullPath(usdFilePath);
                // usdzエクスポート
                // usdaを作るときは中間ファイルを消さない
                ExportUsdz(fullPath, !this.createUsdaFile);
            }
            finally
            {
                this.usdScene = null;
                this.exportContext = null;
                this.IsRecording = false;
            }
        }

        private void CaptureThumbnail(string fileName)
        {
            if (this.thumbnailCamera == null) return;


            string thumbnailDirectory;
            if (string.IsNullOrEmpty(this.exportDirectory))
            {
                thumbnailDirectory = Directory.GetCurrentDirectory();
            }
            else
            {
                thumbnailDirectory = Path.GetFullPath(this.exportDirectory);
            }
            
            if (!Directory.Exists(thumbnailDirectory))
            {
                Directory.CreateDirectory(thumbnailDirectory);
            }
            
            var thumbnailFileName = fileName + ".png";
            var thumbnailFilePath = Path.Combine(thumbnailDirectory, thumbnailFileName);

            var activeTexture = RenderTexture.active;
            var targetTexture = this.thumbnailCamera.targetTexture;
            try
            {
                const int size = 500;
                var tex = new RenderTexture(size, size, 24, RenderTextureFormat.ARGB32);
                this.thumbnailCamera.targetTexture = tex;
                this.thumbnailCamera.Render();

                RenderTexture.active = tex;
                var tex2d = new Texture2D(size, size, TextureFormat.ARGB32, 0, false);
                tex2d.ReadPixels(new Rect(0, 0, size, size), 0, 0);
                var bytes = tex2d.EncodeToPNG();
                File.WriteAllBytes(thumbnailFilePath, bytes);
            }
            finally
            {
                this.thumbnailCamera.targetTexture = targetTexture;
                RenderTexture.active = activeTexture;
            }
        }

        private void Awake()
            => InitUsd.Initialize();
        
        private void LateUpdate()
        {
            if (!this.IsRecording) return;

            if (this.isFirstFrame)
            {
                this.isFirstFrame = false;
                SceneExporter.SyncExportContext(this.exportRoot.gameObject, this.exportContext);
                ExportScene();

                // マテリアルのエクスポートは最初のフレームのみ
                this.exportContext.exportMaterials = false;
                return;
            }

            this.elapsedTime += Time.deltaTime;
            while (this.elapsedTime >= this.timeUnit)
            {
                this.elapsedTime -= this.timeUnit;
                this.currentFrame++;

                if (this.currentFrame * this.timeUnit >= this.recordSec)
                {
                    Stop();
                    return;
                }

                this.usdScene.Time = this.currentFrame;
                ExportScene();
            }
        }

        private void ExportScene()
        {
            var cacheSR = (this.exportRoot.localScale, this.exportRoot.localRotation);

            var localRotation = this.exportRoot.transform.localRotation;
            try
            {
                // Unityはメートル、usdはセンチメートルなので100倍する
                this.exportRoot.localScale = cacheSR.localScale * 100;
                if (this.flipZ)
                {
                    this.exportRoot.localRotation = cacheSR.localRotation * Quaternion.AngleAxis(180f, Vector3.up);
                }
                SceneExporter.Export(this.exportRoot.gameObject, this.exportContext, zeroRootTransform: false);
            }
            finally
            {
                this.exportRoot.localScale = cacheSR.localScale;
                this.exportRoot.localRotation = cacheSR.localRotation;
            }
        }

        public void ExportUsdz(string filePath, bool deleteUsdDirectory)
        {
            InitUsd.Initialize();

            var currentDir = Directory.GetCurrentDirectory();
            var usdFileName = Path.GetFileName(filePath);
            var usdzFileName = Path.GetFileNameWithoutExtension(usdFileName) + ".usdz";

            string usdzDirectroy;
            if (string.IsNullOrEmpty(this.exportDirectory))
            {
                usdzDirectroy = currentDir;
            }
            else
            {
                usdzDirectroy = Path.GetFullPath(this.exportDirectory);
            }
            var usdzFilePath = Path.Combine(usdzDirectroy, usdzFileName);

            try
            {
                // 画像の検索パスを合わせるためにカレントディレクトリを変更する
                // 後で戻し忘れるとUnityが壊れるので注意
                Directory.SetCurrentDirectory(Path.GetDirectoryName(filePath));

                var assetPath = new SdfAssetPath(usdFileName);
                var success = UsdCs.UsdUtilsCreateNewARKitUsdzPackage(assetPath, usdzFilePath);
                if (!success)
                {
                    Debug.LogError("usdzファイルの出力に失敗しました");
                    return;
                }

                Debug.Log($"出力完了: '{usdzFilePath}'");
            }
            finally
            {
                // 変更していたディレクトリを戻す
                Directory.SetCurrentDirectory(currentDir);

                if (deleteUsdDirectory)
                {
                    var d = Path.GetDirectoryName(filePath);
                    // 違うディレクトリを消してしまうとやばいのでチェック
                    if (Path.GetFileName(d).StartsWith("temp-"))
                    {
                        var di = new DirectoryInfo(d);
                        di.Delete(true);
                    }
                }
            }
        }

        private string GetUsdFilePath()
        {
            var recordTargets = this.exportRoot
                .OfType<Transform>();

            return GetUsdFilePath(recordTargets);
        }

        private string GetUsdFilePath(IEnumerable<Transform> targets)
        {
            string fileName;
            if (!string.IsNullOrEmpty(this.exportFileName))
            {
                fileName = this.exportFileName;
            }
            else
            {
                fileName = targets
                    .Select(t => t.name)
                    .FirstOrDefault();
            }

            if (string.IsNullOrEmpty(fileName)) return string.Empty;


            var extension = this.createUsdaFile ? ".usda" : ".usdc";
            var fileNameWithExtension = fileName + extension;


            var filePath = Path.Combine($"temp-{fileName}", fileNameWithExtension);
            if (!string.IsNullOrEmpty(this.exportDirectory))
            {
                filePath = Path.Combine(this.exportDirectory, filePath);
            }

            return filePath;
        }
    }
}
