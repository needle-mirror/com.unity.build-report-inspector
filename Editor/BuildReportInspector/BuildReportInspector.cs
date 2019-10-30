using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEditor.Build.Reporting;
using System.IO;

/// <summary>
/// Custom inspector implementation for UnityEditor.Build.Reporting.BuildReport objects
/// </summary>
[CustomEditor(typeof(BuildReport))]
public class BuildReportInspector : Editor {

    [MenuItem("Window/Open Last Build Report", true)]
    static bool ValidateOpenLastBuild()
    {
        return File.Exists("Library/LastBuild.buildreport");
    }

    [MenuItem("Window/Open Last Build Report")]
    static void OpenLastBuild()
    {
        var buildReportDir = "Assets/BuildReports";
        if (!Directory.Exists(buildReportDir))
            Directory.CreateDirectory(buildReportDir);

        var date = File.GetLastWriteTime("Library/LastBuild.buildreport");
        var assetPath = buildReportDir + "/Build_" + date.ToString("yyyy-dd-MMM-HH-mm-ss") + ".buildreport";
        File.Copy("Library/LastBuild.buildreport", assetPath, true);
        AssetDatabase.ImportAsset(assetPath);
        Selection.objects = new[] { AssetDatabase.LoadAssetAtPath<BuildReport>(assetPath) };
    }

    BuildReport report {
        get
        {
            return target as BuildReport;
        }
    }

    static GUIStyle s_SizeStyle;
    GUIStyle sizeStyle {
        get
        {
            if (s_SizeStyle == null)
                s_SizeStyle = new GUIStyle(GUI.skin.label);
            s_SizeStyle.alignment = TextAnchor.MiddleRight;
            return s_SizeStyle;   
        }
    }

    static Texture2D MakeColorTexture(Color col)
    {
        Color[] pix = new Color[1];
        pix[0] = col;

        Texture2D result = new Texture2D(1, 1);
        result.SetPixels(pix);
        result.Apply();

        return result;
    }

    static GUIStyle s_OddStyle;
    static GUIStyle OddStyle
    {
        get
        {
            if (s_OddStyle == null)
            {
                s_OddStyle = new GUIStyle(GUIStyle.none);
                s_OddStyle.normal.background = MakeColorTexture(new Color(0.5f, 0.5f, 0.5f, 0.1f));
            }
            return s_OddStyle;
        }
    }

    static GUIStyle s_EvenStyle;
    static GUIStyle EvenStyle
    {
        get
        {
            if (s_EvenStyle == null)
            {
                s_EvenStyle = new GUIStyle(GUIStyle.none);
                s_EvenStyle.normal.background = MakeColorTexture(new Color(0.5f, 0.5f, 0.5f, 0.0f));
            }
            return s_EvenStyle;
        }
    }

    static GUIStyle s_DataFileStyle;
    GUIStyle DataFileStyle
    {
        get
        {
            if (s_DataFileStyle == null)
            {
                s_DataFileStyle = new GUIStyle(EditorStyles.foldout);
                s_DataFileStyle.fontStyle = FontStyle.Bold;
            }
            return s_DataFileStyle;
        }
    }

    const int kLineHeight = 20;

    enum ReportDisplayMode
    {
        BuildSteps,
        SourceAssets,
        OutputFiles,
        Stripping,
#if UNITY_2020_1_OR_NEWER
        ScenesUsingAssets,
#endif
    };

    string[] ReportDisplayModeStrings = {
        "BuildSteps",
        "SourceAssets",
        "OutputFiles",
        "Stripping",
#if UNITY_2020_1_OR_NEWER
        "ScenesUsingAssets",
#endif
    };

    enum SourceAssetsDisplayMode
    {
        Size,
        OutputDataFiles,
        ImporterType
    };

    ReportDisplayMode mode;
    SourceAssetsDisplayMode sourceDispMode;

    Vector2 scrollPosition;

    static string FormatTime(System.TimeSpan t)
    {
        return t.Hours + ":" + t.Minutes.ToString("D2") + ":" + t.Seconds.ToString("D2") + "." + t.Milliseconds.ToString("D3");
    }

    /// <summary>
    /// Custom inspector implementation for UnityEditor.Build.Reporting.BuildReport objects
    /// </summary>
    public override void OnInspectorGUI()
    {
        if (report == null)
        {
            EditorGUILayout.HelpBox("No Build Report.", MessageType.Info);
            return;
        }

        EditorGUILayout.LabelField("Report Info");

        EditorGUILayout.LabelField("    Build Name : ", Application.productName);
        EditorGUILayout.LabelField("    Platform : ", report.summary.platform.ToString());
        EditorGUILayout.LabelField("    Total Time : ", FormatTime(report.summary.totalTime));
        EditorGUILayout.LabelField("    Total Size : ", FormatSize(report.summary.totalSize));
        EditorGUILayout.LabelField("    Build Result : ", report.summary.result.ToString());

        mode = (ReportDisplayMode)GUILayout.Toolbar((int)mode, ReportDisplayModeStrings);

        if (mode == ReportDisplayMode.SourceAssets)
            sourceDispMode = (SourceAssetsDisplayMode)EditorGUILayout.EnumPopup("Sort by:", sourceDispMode);

        scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);
        switch(mode)
        {
            case ReportDisplayMode.BuildSteps:
                OnBuildStepGUI();
                break;
            case ReportDisplayMode.SourceAssets:
                OnAssetsGUI();
                break;
            case ReportDisplayMode.OutputFiles:
                OnOutputFilesGUI();
                break;
            case ReportDisplayMode.Stripping:
                OnStrippingGUI();
                break;
#if UNITY_2020_1_OR_NEWER
            case ReportDisplayMode.ScenesUsingAssets:
                OnScenesUsingAssetsGUI();
                break;
#endif
        }
        EditorGUILayout.EndScrollView();
    }

    static List<LogType> ErrorLogTypes = new List<LogType> { LogType.Error, LogType.Assert, LogType.Exception };

    static LogType WorseLogType(LogType log1, LogType log2)
    {
        if (ErrorLogTypes.Contains(log1) || ErrorLogTypes.Contains(log2))
            return LogType.Error;
        if (log1 == LogType.Warning || log2 == LogType.Warning)
            return LogType.Warning;
        return LogType.Log;
    }

    class BuildStepNode
    {
        public BuildStep? step;
        public int depth;
        public List<BuildStepNode> children;
        public LogType worstChildrenLogType;
        public bool foldoutState;

        public BuildStepNode(BuildStep? _step, int _depth)
        {
            step = _step;
            depth = _depth;
            children = new List<BuildStepNode>();

            worstChildrenLogType = LogType.Log;
            if(step.HasValue)
            {
                foreach (var message in step.Value.messages)
                {
                    worstChildrenLogType = message.type; // Warning
                    if (ErrorLogTypes.Contains(message.type))
                        break; // Error
                }
            }

            foldoutState = false;
        }

        public void UpdateWorstChildrenLogType()
        {
            foreach(var child in children)
            {
                child.UpdateWorstChildrenLogType();
                worstChildrenLogType = WorseLogType(worstChildrenLogType, child.worstChildrenLogType);
            }
        }

        public void LayoutGUI(ref bool switchBackgroundColor, float indentPixels)
        {
            switchBackgroundColor = !switchBackgroundColor;
            GUILayout.BeginVertical(switchBackgroundColor ? OddStyle : EvenStyle);
            GUILayout.BeginHorizontal();                
            GUILayout.Space(10 + indentPixels);

            if (children.Any() || (step.HasValue && step.Value.messages.Any()))
            {
                if (worstChildrenLogType != LogType.Log)
                {
                    var icon = "console.warnicon.sml";
                    if (worstChildrenLogType != LogType.Warning)
                        icon = icon = "console.erroricon.sml";
                    foldoutState = EditorGUILayout.Foldout(foldoutState, EditorGUIUtility.TrTextContentWithIcon(step?.name, icon));
                }
                else
                {
                    foldoutState = EditorGUILayout.Foldout(foldoutState, step?.name);
                }
            }
            else
                GUILayout.Label(step?.name);

            GUILayout.FlexibleSpace();
            GUILayout.Label(step?.duration.Hours + ":" + step?.duration.Minutes.ToString("D2") + ":" + step?.duration.Seconds.ToString("D2") + "." + step?.duration.Milliseconds.ToString("D3"));
            GUILayout.EndHorizontal();

            if (foldoutState)
            {
                if (step.HasValue)
                {
                    foreach (var message in step.Value.messages)
                    {
                        var icon = "console.infoicon.sml";
                        var oldCol = GUI.color;
                        switch (message.type)
                        {
                            case LogType.Warning:
                                GUI.color = Color.yellow;
                                icon = "console.warnicon.sml";
                                break;
                            case LogType.Error:
                            case LogType.Exception:
                            case LogType.Assert:
                                GUI.color = Color.red;
                                icon = "console.erroricon.sml";
                                break;
                        }
                        GUILayout.BeginHorizontal();
                        {
                            GUILayout.Space(20 + indentPixels);
                            GUILayout.Label(EditorGUIUtility.IconContent(icon), GUILayout.ExpandWidth(false));
                            EditorGUILayout.LabelField(new GUIContent(message.content, message.content));
                        }
                        GUILayout.EndHorizontal();
                        GUI.color = oldCol;
                    }
                }

                foreach (var child in children)
                    child.LayoutGUI(ref switchBackgroundColor, indentPixels + 20);
            }
            GUILayout.EndVertical();
        }
    }
    BuildStepNode rootStepNode = new BuildStepNode(null, -1);

    void OnBuildStepGUI()
    {
        if(!rootStepNode.children.Any())
        {
            // re-create steps hierarchy
            Stack<BuildStepNode> branch = new Stack<BuildStepNode>();
            branch.Push(rootStepNode);
            foreach (var step in report.steps)
            {
                while (branch.Peek().depth >= step.depth)
                {
                    branch.Pop();
                }

                while (branch.Peek().depth < (step.depth - 1))
                {
                    var intermediateNode = new BuildStepNode(null, branch.Count - 1);
                    branch.Peek().children.Add(intermediateNode);
                    branch.Push(intermediateNode);
                }

                var stepNode = new BuildStepNode(step, step.depth);
                branch.Peek().children.Add(stepNode);
                branch.Push(stepNode);
            }

            rootStepNode.UpdateWorstChildrenLogType();

            // expand first step, usually "Build player"
            if (rootStepNode.children.Any())
                rootStepNode.children[0].foldoutState = true;
        }

        bool odd = false;
        foreach(var stepNode in rootStepNode.children)
            stepNode.LayoutGUI(ref odd, 0);
    }

    string FormatSize(ulong size)
    {
        if (size < 1024)
            return size + " B";
        if (size < 1024*1024)
            return (size/1024.00).ToString("F2") + " KB";
        if (size < 1024 * 1024 * 1024)
            return (size / (1024.0 * 1024.0)).ToString("F2") + " MB";
        return (size / (1024.0 * 1024.0 * 1024.0)).ToString("F2") + " GB";
    }

    struct AssetEntry
    {
        public string path;
        public int size;
        public string outputFile;
        public string type;
        public Texture icon;
    }

    void ShowAssets(List<AssetEntry> assets, ref float vPos, string fileFilter = null, string typeFilter = null)
    {
        GUILayout.BeginVertical();
        var odd = false;
        foreach (var entry in assets)
        {
            if (fileFilter != null && fileFilter != entry.outputFile)
                continue;

            if (typeFilter != null && typeFilter != entry.type)
                continue;

            if (vPos >= -kLineHeight && vPos <= Screen.height)
            {
                GUILayout.BeginHorizontal(odd ? OddStyle : EvenStyle);

                GUILayout.Label(entry.icon, GUILayout.MaxHeight(16), GUILayout.Width(20));
                if (GUILayout.Button(new GUIContent(Path.GetFileName(entry.path), entry.path), GUI.skin.label, GUILayout.MaxWidth(EditorGUIUtility.currentViewWidth - 110)))
                    EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<Object>(entry.path));
                GUILayout.Label(FormatSize((ulong)entry.size), sizeStyle);
                GUILayout.EndHorizontal();
            }
            else
            {
                GUILayout.Space(kLineHeight);
            }
            vPos += kLineHeight;
            odd = !odd;
        }
        GUILayout.EndVertical();
    }

    Dictionary<string, bool> assetsFoldout = new Dictionary<string, bool>();
    List<AssetEntry> assets = null;
    Dictionary<string, int> outputFiles = null;
    Dictionary<string, int> assetTypes = null;


    void OnAssetsGUI()
    {
        float vPos = -scrollPosition.y;
        var appendices = serializedObject.FindProperty("m_Appendices");
        if (appendices != null)
        {
            if (assets == null)
            {
                assets = new List<AssetEntry>();
                outputFiles = new Dictionary<string, int>();
                assetTypes = new Dictionary<string, int>();
                for (int i = 0; i < appendices.arraySize; i++)
                {
                    var appendix = appendices.GetArrayElementAtIndex(i);
                    if (appendix.objectReferenceValue.GetType() == typeof(Object))
                    {
                        var appendixSO = new SerializedObject(appendix.objectReferenceValue);
                        if (appendixSO.FindProperty("m_ShortPath") != null)
                        {
                            var pathProperty = appendixSO.FindProperty("m_ShortPath");
                            if (pathProperty != null)
                            {
                                var path = pathProperty.stringValue;
                                var contents = appendixSO.FindProperty("m_Contents");
                                outputFiles[path] = 0;
                                var totalSizeProp = appendixSO.FindProperty("m_Overhead");
                                if (totalSizeProp != null)
                                    outputFiles[path] = totalSizeProp.intValue;
                                if (contents != null)
                                {
                                    for (int j = 0; j < contents.arraySize; j++)
                                    {
                                        var entry = contents.GetArrayElementAtIndex(j);
                                        var entryPathProp = entry.FindPropertyRelative("buildTimeAssetPath");
                                        if (entryPathProp != null)
                                        {
                                            var entryPath = entryPathProp.stringValue;
                                            if (!string.IsNullOrEmpty(entryPath))
                                            {
                                                AssetEntry assetEntry = new AssetEntry();
                                                var asset = AssetImporter.GetAtPath(entryPath);
                                                var type = asset != null? asset.GetType().Name : "Unknown";
                                                if (type.EndsWith("Importer"))
                                                    type = type.Substring(0, type.Length - 8);
                                                var sizeProp = entry.FindPropertyRelative("packedSize");
                                                if (!assetTypes.ContainsKey(type))
                                                    assetTypes[type] = 0;
                                                if (sizeProp != null)
                                                {
                                                    assetEntry.size = sizeProp.intValue;
                                                    outputFiles[path] += sizeProp.intValue;
                                                    assetTypes[type] += sizeProp.intValue;
                                                }
                                                assetEntry.icon = AssetDatabase.GetCachedIcon(entryPath);
                                                assetEntry.outputFile = path;
                                                assetEntry.type = type;
                                                assetEntry.path = entryPath;
                                                assets.Add(assetEntry);
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
                assets = assets.OrderBy(p => -p.size).ToList();
                outputFiles = outputFiles.OrderBy(p => -p.Value).ToDictionary(x => x.Key, x => x.Value);
                assetTypes = assetTypes.OrderBy(p => -p.Value).ToDictionary(x => x.Key, x => x.Value);
            }
            switch (sourceDispMode)
            {
                case SourceAssetsDisplayMode.Size:
                    ShowAssets(assets, ref vPos);
                    break;
                case SourceAssetsDisplayMode.OutputDataFiles:
                    foreach (var outputFile in outputFiles)
                    {
                        if (!assetsFoldout.ContainsKey(outputFile.Key))
                            assetsFoldout[outputFile.Key] = false;

                        GUILayout.BeginHorizontal();
                        GUILayout.Space(10);
                        assetsFoldout[outputFile.Key] = EditorGUILayout.Foldout(assetsFoldout[outputFile.Key], outputFile.Key, DataFileStyle);
                        GUILayout.Label(FormatSize((ulong)outputFile.Value), sizeStyle);
                        GUILayout.EndHorizontal();

                        vPos += kLineHeight;

                        if (assetsFoldout[outputFile.Key])
                            ShowAssets(assets, ref vPos, outputFile.Key);
                    }
                    break;
                case SourceAssetsDisplayMode.ImporterType:
                    foreach (var outputFile in assetTypes)
                    {
                        if (!assetsFoldout.ContainsKey(outputFile.Key))
                            assetsFoldout[outputFile.Key] = false;

                        GUILayout.BeginHorizontal();
                        GUILayout.Space(10);
                        assetsFoldout[outputFile.Key] = EditorGUILayout.Foldout(assetsFoldout[outputFile.Key], outputFile.Key, DataFileStyle);
                        GUILayout.Label(FormatSize((ulong)outputFile.Value), sizeStyle);
                        GUILayout.EndHorizontal();

                        vPos += kLineHeight;

                        if (assetsFoldout[outputFile.Key])
                            ShowAssets(assets, ref vPos, null, outputFile.Key);
                    }             
                    break;
            }                           
        }
        else 
            GUILayout.Label("No Appendices property found");
    }

    void OnOutputFilesGUI()
    {
        var longestCommonRoot = report.files[0].path;
        var tempRoot = Path.GetFullPath("Temp");
        foreach (var file in report.files)
        {
            if (file.path.StartsWith(tempRoot))
                continue;
            for (int i = 0; i < longestCommonRoot.Length && i < file.path.Length; i++)
            {
                if (longestCommonRoot[i] != file.path[i])
                {
                    longestCommonRoot = longestCommonRoot.Substring(0, i);
                    break;
                }
            }
        }
        bool odd = false;
        foreach (var file in report.files)
        {
            if (file.path.StartsWith(tempRoot))
                continue;
            GUILayout.BeginHorizontal(odd? OddStyle:EvenStyle);
            odd = !odd;
            GUILayout.Label(new GUIContent(file.path.Substring(longestCommonRoot.Length), file.path), GUILayout.MaxWidth(EditorGUIUtility.currentViewWidth - 260));
            GUILayout.Label(file.role);
            GUILayout.Label(FormatSize(file.size), sizeStyle);
            GUILayout.EndHorizontal();

        }
    }

    Dictionary<string, Texture> strippingIcons = new Dictionary<string, Texture>();
    Dictionary<string, int> strippingSizes = new Dictionary<string, int>();

    static Dictionary<string, Texture> iconCache = new Dictionary<string, Texture>();
    Texture StrippingEntityIcon(string iconString)
    {
        if (iconCache.ContainsKey(iconString))
            return iconCache[iconString];

        if (iconString.StartsWith("class/"))
        {
            var type = System.Type.GetType("UnityEngine." + iconString.Substring(6) + ",UnityEngine");
            if (type != null)
            {
                var image = EditorGUIUtility.ObjectContent(null, System.Type.GetType("UnityEngine." + iconString.Substring(6) + ",UnityEngine")).image;
                if (image != null)
                    iconCache[iconString] = image;
            }
        }
        if (iconString.StartsWith("package/"))
        {
            var path = EditorApplication.applicationContentsPath + "/Resources/PackageManager/Editor/" + iconString.Substring(8) + "/.icon.png";
            if (File.Exists(path))
            {
                Texture2D tex = new Texture2D(2, 2);
                tex.LoadImage(File.ReadAllBytes(path));
                iconCache[iconString] = tex;
            }
        }

        if (!iconCache.ContainsKey(iconString))
            iconCache[iconString] = EditorGUIUtility.ObjectContent(null, typeof(DefaultAsset)).image;

        return iconCache[iconString];
    }

    Dictionary<string, bool> strippingReasonsFoldout = new Dictionary<string, bool>();
    void StrippingEntityGUI(string entity, ref bool odd)
    {
        GUILayout.BeginHorizontal(odd ? OddStyle : EvenStyle);
        odd = !odd;
        GUILayout.Space(15); 
        var reasons = report.strippingInfo.GetReasonsForIncluding(entity);
        if (!strippingIcons.ContainsKey(entity))
            strippingIcons[entity] = StrippingEntityIcon(entity);
        var icon = strippingIcons[entity];
        if (reasons.Any())
        {
            if (!strippingReasonsFoldout.ContainsKey(entity))
                strippingReasonsFoldout[entity] = false;
            strippingReasonsFoldout[entity] = EditorGUILayout.Foldout(strippingReasonsFoldout[entity], new GUIContent(entity, icon));
        }
        else
            EditorGUILayout.LabelField(new GUIContent(entity, icon), GUILayout.Height(16), GUILayout.MaxWidth(1000));

        GUILayout.FlexibleSpace();

        if (strippingSizes.ContainsKey(entity) && strippingSizes[entity] != 0)
            GUILayout.Label(FormatSize((ulong)strippingSizes[entity]), sizeStyle, GUILayout.Width(100));

        GUILayout.EndHorizontal();

        if (strippingReasonsFoldout.ContainsKey(entity) && strippingReasonsFoldout[entity])
        {
            EditorGUI.indentLevel++;
            foreach (var reason in reasons)
                StrippingEntityGUI(reason, ref odd);
            EditorGUI.indentLevel--;
        }
    }


    void OnStrippingGUI()
    {
        if (report.strippingInfo == null)
        {
            EditorGUILayout.HelpBox("No stripping info.", MessageType.Info);
            return;
        }

        var so = new SerializedObject(report.strippingInfo);
        var serializedDependencies = so.FindProperty("serializedDependencies");
        //var hasSizes = false;
        if (serializedDependencies != null)
        {
            for (int i = 0; i < serializedDependencies.arraySize; i++)
            {
                var sp = serializedDependencies.GetArrayElementAtIndex(i);
                var depKey = sp.FindPropertyRelative("key").stringValue;
                strippingIcons[depKey] = StrippingEntityIcon(sp.FindPropertyRelative("icon").stringValue);
                strippingSizes[depKey] = sp.FindPropertyRelative("size").intValue;
                //if (strippingSizes[depKey] != 0)
                //    hasSizes = true;
            }
        }

        var analyzeMethod = report.strippingInfo.GetType().GetMethod("Analyze", System.Reflection.BindingFlags.FlattenHierarchy | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public);
        if (/*!hasSizes &&*/ analyzeMethod != null)
        {
            if (GUILayout.Button("Analyze size"))
                analyzeMethod.Invoke(report.strippingInfo, null);
        }

        var odd = false;
        foreach (var module in report.strippingInfo.includedModules)
        {
            StrippingEntityGUI(module, ref odd);
        }
    }

#if UNITY_2020_1_OR_NEWER
    class ScenesUsingAssetGUI
    {
        public string assetPath;
        public string[] scenePaths;
        public bool foldoutState;
    }
    List<ScenesUsingAssetGUI> scenesUsingAssetGUIs = new List<ScenesUsingAssetGUI>();

    void OnScenesUsingAssetsGUI()
    {
        if (report.scenesUsingAssets == null || report.scenesUsingAssets.Length==0 || report.scenesUsingAssets[0] == null || report.scenesUsingAssets[0].list==null || report.scenesUsingAssets[0].list.Length==0 )
        {
            EditorGUILayout.HelpBox("No info about which scenes are using assets in the build. Did you use BuildOptions.DetailedBuildReport?", MessageType.Info);
            return;
        }

        // re-create list of scenes using assets
        if(!scenesUsingAssetGUIs.Any())
        {
            foreach (var scenesUsingAsset in report.scenesUsingAssets[0].list)
                scenesUsingAssetGUIs.Add(new ScenesUsingAssetGUI { assetPath = scenesUsingAsset.assetPath, scenePaths = scenesUsingAsset.scenePaths, foldoutState = true});
        }

        bool odd = true;
        foreach (var scenesUsingAssetGUI in scenesUsingAssetGUIs)
        {
            odd = !odd;
            GUILayout.BeginVertical(odd ? OddStyle : EvenStyle);

            GUILayout.BeginHorizontal(odd ? OddStyle : EvenStyle);
            GUILayout.Space(10);
            scenesUsingAssetGUI.foldoutState = EditorGUILayout.Foldout(scenesUsingAssetGUI.foldoutState, scenesUsingAssetGUI.assetPath);
            GUILayout.EndHorizontal();

            if(scenesUsingAssetGUI.foldoutState)
            {
                foreach (var scenePath in scenesUsingAssetGUI.scenePaths)
                {
                    odd = !odd;
                    GUILayout.BeginHorizontal(odd ? OddStyle : EvenStyle);
                    GUILayout.Space(20);
                    GUILayout.Label(scenePath);
                    GUILayout.EndHorizontal();
                }
            }
            
            GUILayout.EndVertical();
        }
    }
#endif // UNITY_2020_1_OR_NEWER
}
