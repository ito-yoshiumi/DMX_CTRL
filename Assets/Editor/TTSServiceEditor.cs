using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;
using Encounter.Scenario;

[CustomEditor(typeof(TTSService))]
public class TTSServiceEditor : Editor
{
    private List<TTSService.SpeakerInfo> _speakers = new();
    private string _statusMessage = "まだ話者一覧を取得していません。";
    private bool _isFetching;

    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        EditorGUILayout.Space(10);
        EditorGUILayout.LabelField("話者一覧 (VOICEVOX/COEIROINK)", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("「話者一覧を取得」を押すと、現在起動中のVOICEVOX/COEIROINKエンジンから利用可能な話者とスタイルを取得します。任意のスタイルの「適用」を押すとSpeaker IDに反映されます。", MessageType.Info);

        using (new EditorGUI.DisabledScope(_isFetching))
        {
            if (GUILayout.Button(_isFetching ? "取得中..." : "話者一覧を取得", GUILayout.Height(26)))
            {
                FetchSpeakers();
            }
        }

        if (!string.IsNullOrEmpty(_statusMessage))
        {
            EditorGUILayout.HelpBox(_statusMessage, MessageType.None);
        }

        if (_speakers != null && _speakers.Count > 0)
        {
            var service = (TTSService)target;

            foreach (var speaker in _speakers)
            {
                using (new EditorGUILayout.VerticalScope("box"))
                {
                    EditorGUILayout.LabelField(speaker.name, EditorStyles.boldLabel);
                    EditorGUI.indentLevel++;
                    if (speaker.styles != null)
                    {
                        foreach (var style in speaker.styles)
                        {
                            EditorGUILayout.BeginHorizontal();
                            EditorGUILayout.LabelField($"[{style.id}] {style.name}");
                            if (GUILayout.Button("適用", GUILayout.Width(60)))
                            {
                                ApplySpeaker(service, style.id);
                            }
                            EditorGUILayout.EndHorizontal();
                        }
                    }
                    EditorGUI.indentLevel--;
                }
            }
        }
    }

    private void FetchSpeakers()
    {
        var service = (TTSService)target;
        string url = $"{service.engineBaseUrl.TrimEnd('/')}/speakers";
        _isFetching = true;
        _statusMessage = $"取得中: {url}";
        Repaint();

        var request = UnityWebRequest.Get(url);
        request.timeout = Mathf.CeilToInt(service.connectionTimeout);

        var asyncOperation = request.SendWebRequest();
        asyncOperation.completed += _ =>
        {
            _isFetching = false;
            try
            {
                if (request.result == UnityWebRequest.Result.Success)
                {
                    string json = request.downloadHandler.text;
                    if (!string.IsNullOrEmpty(json))
                    {
                        string wrapped = $"{{\"items\":{json}}}";
                        var wrapper = JsonUtility.FromJson<SpeakerListWrapper>(wrapped);
                        _speakers = wrapper?.items?.ToList() ?? new List<TTSService.SpeakerInfo>();
                        _statusMessage = $"取得成功: {_speakers.Count}件";
                    }
                    else
                    {
                        _statusMessage = "エンジンから空のレスポンスが返されました。";
                    }
                }
                else
                {
                    _statusMessage = $"取得失敗: {request.error}";
                }
            }
            catch (Exception ex)
            {
                _statusMessage = $"解析失敗: {ex.Message}";
            }
            finally
            {
                request.Dispose();
                EditorApplication.delayCall += Repaint;
            }
        };
    }

    private void ApplySpeaker(TTSService service, int speakerId)
    {
        Undo.RecordObject(service, "Change Speaker ID");
        service.speakerId = speakerId;
        EditorUtility.SetDirty(service);
        _statusMessage = $"Speaker ID {speakerId} を設定しました。";
        Repaint();
    }

    [Serializable]
    private class SpeakerListWrapper
    {
        public TTSService.SpeakerInfo[] items;
    }
}

