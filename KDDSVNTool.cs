using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using UnityEditor;
using UnityEngine;

public class SVNMultiPathTool : EditorWindow
{
    [MenuItem("Tools/SVN自定义更新工具",false,10)]
    public static void ShowWindow()
    {
        GetWindow<SVNMultiPathTool>("SVN自定义更新工具");
    }

    // 存储路径列表
    [Serializable]
    public class PathEntry
    {
        public string path = "";
        public bool includeInOperations = true;
    }

    [SerializeField] private List<PathEntry> pathEntries = new List<PathEntry>();
    private Vector2 scrollPosition;
    private Vector2 resultScrollPosition;
    
    // 批量操作状态
    private bool isOperating = false;
    private bool cancelRequested = false;
    private int currentOperationIndex = 0;
    private int totalOperations = 0;
    private StringBuilder operationResults = new StringBuilder();
    private string currentOperationName = "";
    
    // 初始化
    private void OnEnable()
    {
        // 加载保存的路径
        LoadPaths();
        
        // 如果没有路径，添加一个默认空路径
        if (pathEntries.Count == 0)
        {
            AddNewPath();
        }
    }
    
    // 确保在窗口关闭时停止操作
    private void OnDestroy()
    {
        cancelRequested = true;
    }

    // 绘制UI
    private void OnGUI()
    {
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("SVN自定义更新工具", EditorStyles.boldLabel);
        EditorGUILayout.Space();

        // 按钮区域
        EditorGUILayout.BeginHorizontal();
        {
            GUI.enabled = !isOperating;
            if (GUILayout.Button("添加路径", GUILayout.Width(80)))
            {
                AddNewPath();
            }

            if (GUILayout.Button("清理所有", GUILayout.Width(80)))
            {
                CleanupAllPaths();
            }

            if (GUILayout.Button("更新所有", GUILayout.Width(80)))
            {
                UpdateAllPaths();
            }

            if (GUILayout.Button("保存配置", GUILayout.Width(80)))
            {
                SavePaths();
            }
            GUI.enabled = true;
            
            // 取消按钮（仅在操作中显示）
            // if (isOperating)
            // {
            //     if (GUILayout.Button("取消操作", GUILayout.Width(80)))
            //     {
            //         cancelRequested = true;
            //     }
            // }
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space();
        
        // 显示操作进度
        // if (isOperating)
        // {
        //     float progress = totalOperations > 0 ? (float)currentOperationIndex / totalOperations : 0;
        //     EditorGUI.ProgressBar(EditorGUILayout.GetControlRect(), progress, 
        //         $"{currentOperationName} ({currentOperationIndex}/{totalOperations})");
        // }

        // 路径列表
        scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition, GUILayout.Height(200));
        {
            for (int i = 0; i < pathEntries.Count; i++)
            {
                EditorGUILayout.BeginHorizontal();
                {
                    // 路径选择
                    EditorGUILayout.LabelField("路径 " + (i + 1), GUILayout.Width(50));
                    pathEntries[i].path = EditorGUILayout.TextField(pathEntries[i].path);
                    
                    GUI.enabled = !isOperating;
                    if (GUILayout.Button("选择", GUILayout.Width(40)))
                    {
                        string selectedPath = EditorUtility.OpenFolderPanel("选择文件夹", Application.dataPath, "");
                        if (!string.IsNullOrEmpty(selectedPath))
                        {
                            pathEntries[i].path = selectedPath;
                        }
                    }
                    
                    // 包含在操作中
                    pathEntries[i].includeInOperations = EditorGUILayout.Toggle(
                        pathEntries[i].includeInOperations, GUILayout.Width(20));
                    
                    // 单独操作按钮
                    if (GUILayout.Button("清理", GUILayout.Width(40)))
                    {
                        CleanupPath(pathEntries[i].path);
                    }
                    
                    if (GUILayout.Button("更新", GUILayout.Width(40)))
                    {
                        UpdatePath(pathEntries[i].path);
                    }
                    
                    // 删除按钮
                    if (GUILayout.Button("X", GUILayout.Width(20)))
                    {
                        pathEntries.RemoveAt(i);
                        i--; // 调整索引
                    }
                    GUI.enabled = true;
                }
                EditorGUILayout.EndHorizontal();
            }
        }
        EditorGUILayout.EndScrollView();

        // 结果显示区域
        // if (operationResults.Length > 0)
        // {
        //
        // }
        
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("操作结果:", EditorStyles.boldLabel);
        resultScrollPosition = EditorGUILayout.BeginScrollView(resultScrollPosition, GUILayout.Height(150));
        {
            EditorGUILayout.TextArea(operationResults.ToString(), GUILayout.ExpandHeight(true));
        }
        EditorGUILayout.EndScrollView();
            
        if (GUILayout.Button("清除结果"))
        {
            operationResults.Clear();
        }
        

        EditorGUILayout.Space();
        
        // 底部说明
        EditorGUILayout.HelpBox(
            "1. 点击「添加路径」增加新的路径\n" +
            "2. 使用「选择」按钮浏览文件夹或手动输入路径\n" +
            "3. 取消勾选框可排除该路径不被批量操作影响\n" +
            "4. 使用右侧按钮可对单个路径执行操作\n" +
            "5. 顶部按钮用于批量操作所有已勾选的路径\n" +
            "6. 完成后点击「保存配置」保存当前路径列表", 
            MessageType.Info);
    }

    // 添加新路径
    private void AddNewPath()
    {
        pathEntries.Add(new PathEntry());
    }

    // 清理单个路径
    private void CleanupPath(string path)
    {
        if (!Directory.Exists(path))
        {
            EditorUtility.DisplayDialog("错误", "路径不存在: " + path, "确定");
            return;
        }

        string result = ExecuteSVNCommand("cleanup", path, "清理: " + Path.GetFileName(path));
        AddOperationResult($"清理 {path}: {result}");
    }

    // 更新单个路径
    private void UpdatePath(string path)
    {
        if (!Directory.Exists(path))
        {
            EditorUtility.DisplayDialog("错误", "路径不存在: " + path, "确定");
            return;
        }

        string result = ExecuteSVNCommand("update", path, "更新: " + Path.GetFileName(path));
        AddOperationResult($"更新 {path}: {result}");
    }

    // 清理所有已勾选的路径
    private void CleanupAllPaths()
    {
        if (!EditorUtility.DisplayDialog("确认", "确定要清理所有已勾选的路径吗？", "是", "否"))
            return;

        List<string> pathsToProcess = new List<string>();
        for (int i = 0; i < pathEntries.Count; i++)
        {
            if (pathEntries[i].includeInOperations && !string.IsNullOrEmpty(pathEntries[i].path))
            {
                pathsToProcess.Add(pathEntries[i].path);
            }
        }

        if (pathsToProcess.Count == 0)
        {
            EditorUtility.DisplayDialog("提示", "没有可清理的路径", "确定");
            return;
        }

        StartBatchOperation("清理", pathsToProcess, CleanupPath);
    }

    // 更新所有已勾选的路径
    private void UpdateAllPaths()
    {
        if (!EditorUtility.DisplayDialog("确认", "确定要更新所有已勾选的路径吗？", "是", "否"))
            return;

        List<string> pathsToProcess = new List<string>();
        for (int i = 0; i < pathEntries.Count; i++)
        {
            if (pathEntries[i].includeInOperations && !string.IsNullOrEmpty(pathEntries[i].path))
            {
                pathsToProcess.Add(pathEntries[i].path);
            }
        }

        if (pathsToProcess.Count == 0)
        {
            EditorUtility.DisplayDialog("提示", "没有可更新的路径", "确定");
            return;
        }

        StartBatchOperation("更新", pathsToProcess, UpdatePath);
    }

    // 开始批量操作
    private void StartBatchOperation(string operationName, List<string> paths, Action<string> operation)
    {
        isOperating = true;
        cancelRequested = false;
        currentOperationIndex = 0;
        totalOperations = paths.Count;
        currentOperationName = operationName;
        operationResults.Clear();
        
        // 使用EditorApplication.delayCall来在下一帧开始操作，避免阻塞UI
        EditorApplication.delayCall += () => ProcessBatchOperations(paths, operation);
    }

    // 处理批量操作
    private void ProcessBatchOperations(List<string> paths, Action<string> operation)
    {
        if (cancelRequested || currentOperationIndex >= paths.Count)
        {
            EditorUtility.ClearProgressBar();
            FinishBatchOperation();
            return;
        }

        string path = paths[currentOperationIndex];
        try
        {
            // EditorUtility.DisplayProgressBar("title", "执行中...", (float)currentOperationIndex/totalOperations);
            if (EditorUtility.DisplayCancelableProgressBar("Title", 
                    $"执行中:  ({currentOperationIndex}/{totalOperations})", 
                    (float)currentOperationIndex/totalOperations))
            {
                cancelRequested = true;
            }
            operation(path);
        }
        catch (Exception ex)
        {
            EditorUtility.ClearProgressBar();
            AddOperationResult($"操作失败 {path}: {ex.Message}");
        }

        currentOperationIndex++;
        
        // 继续处理下一个操作
        if (currentOperationIndex < paths.Count && !cancelRequested)
        {
            EditorApplication.delayCall += () => ProcessBatchOperations(paths, operation);
        }
        else
        {
            EditorUtility.ClearProgressBar();
            FinishBatchOperation();
        }
        
        // 刷新UI
        Repaint();
    }

    // 完成批量操作
    private void FinishBatchOperation()
    {
        isOperating = false;
        currentOperationName = "";
        
        if (cancelRequested)
        {
            AddOperationResult("操作已取消");
        }
        else
        {
            AddOperationResult($"所有操作已完成，共处理 {currentOperationIndex} 个路径");
        }
        
        if (!EditorApplication.isUpdating)
        {
            AssetDatabase.Refresh();
            AssetDatabase.SaveAssets();
        }
        Repaint();
    }

    // 添加操作结果
    private void AddOperationResult(string result)
    {
        operationResults.AppendLine($"[{DateTime.Now:HH:mm:ss}] {result}");
        Repaint();
    }

    // 执行SVN命令
    private string ExecuteSVNCommand(string command, string path, string progressTitle)
    {
        try
        {
            // 显示进度条
            // EditorUtility.DisplayProgressBar(progressTitle, "执行中...", 0.5f);

            // 创建进程信息
            ProcessStartInfo processInfo = new ProcessStartInfo
            {
                FileName = "svn",
                Arguments = $"{command} \"{path}\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            // 启动进程
            using (Process process = Process.Start(processInfo))
            {
                // 读取输出
                string output = process.StandardOutput.ReadToEnd();
                string error = process.StandardError.ReadToEnd();

                process.WaitForExit();

                // 隐藏进度条
                EditorUtility.ClearProgressBar();

                // 返回结果
                if (process.ExitCode == 0)
                {
                    if (string.IsNullOrEmpty(output))
                        output = "操作成功完成";
                    
                    return $"成功: {output}";
                }
                else
                {
                    return $"失败: {error}";
                }
            }
        }
        catch (Exception ex)
        {
            // EditorUtility.ClearProgressBar();
            return $"异常: {ex.Message}";
        }
    }

    // 保存路径列表
    private void SavePaths()
    {
        string data = JsonUtility.ToJson(new SerializationWrapper<PathEntry>(pathEntries), true);
        File.WriteAllText(GetConfigPath(), data);
        AddOperationResult("配置已保存");
    }

    // 加载路径列表
    private void LoadPaths()
    {
        string configPath = GetConfigPath();
        if (File.Exists(configPath))
        {
            string data = File.ReadAllText(configPath);
            pathEntries = JsonUtility.FromJson<SerializationWrapper<PathEntry>>(data).list;
        }
    }

    // 获取配置文件路径
    private string GetConfigPath()
    {
        return Application.dataPath + "/../ProjectSettings/SVNMultiPathConfig.json";
    }

    // 用于序列化列表的包装类
    [Serializable]
    private class SerializationWrapper<T>
    {
        public List<T> list;
        
        public SerializationWrapper(List<T> list)
        {
            this.list = list;
        }
    }
}
