using System;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace HexitEngine
{

    // 💡 사운드를 구분하기 위한 타입 추가
    public enum SoundType { SFX, BGM }

    [CreateAssetMenu(fileName = "New SoundData", menuName = "Audio/SoundData")]
    [Serializable]
    public class SoundData : ScriptableObject
    {
        public string id;
        public SoundType soundType = SoundType.SFX; // SFX인지 BGM인지 선택하는 변수

        public AudioClip audioClip;
        [Range(0f, 1f)] public float volume = 1f;
        public float pitch = 1f;

        public bool isRandomPitch;
        public float minPitch = 0.8f;
        public float maxPitch = 1.2f;
    }

    // ==========================================================
    // 커스텀 에디터 (빌드 시 제외)
    // ==========================================================
#if UNITY_EDITOR
    [CustomEditor(typeof(SoundData))]
    public class SoundDataEditor : UnityEditor.Editor
    {
        private SoundData data;
        private AudioSource previewSource;

        private void OnEnable()
        {
            data = (SoundData)target;
            if (data != null && data.id != data.name)
            {
                data.id = data.name;
                EditorUtility.SetDirty(data);
            }
        }

        private void OnDisable()
        {
            // 인스펙터 창이 바뀌면 재생 중이던 소리 정리
            StopPreview();
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            // 1. 이름 및 ID 변경 UI
            EditorGUI.BeginChangeCheck();
            string newName = EditorGUILayout.DelayedTextField("ID (File Name)", data.name);

            if (EditorGUI.EndChangeCheck())
            {
                if (!string.IsNullOrWhiteSpace(newName) && newName != data.name)
                {
                    string assetPath = AssetDatabase.GetAssetPath(data);
                    AssetDatabase.RenameAsset(assetPath, newName);

                    data.name = newName;
                    data.id = newName;

                    EditorUtility.SetDirty(data);
                    AssetDatabase.SaveAssets();

                    GUIUtility.ExitGUI(); // UI 꼬임 방지용 탈출
                }
            }

            // 2. 사운드 타입(SFX, BGM) 설정 UI
            EditorGUILayout.PropertyField(serializedObject.FindProperty("soundType"));
            EditorGUILayout.Space();

            // 3. 사운드 세팅 필드들
            EditorGUILayout.PropertyField(serializedObject.FindProperty("audioClip"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("volume"));

            EditorGUILayout.Space();

            SerializedProperty isRandomPitchProp = serializedObject.FindProperty("isRandomPitch");
            EditorGUILayout.PropertyField(isRandomPitchProp, new GUIContent("Random Pitch"));

            if (isRandomPitchProp.boolValue)
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.PrefixLabel("Pitch Range");

                SerializedProperty minProp = serializedObject.FindProperty("minPitch");
                SerializedProperty maxProp = serializedObject.FindProperty("maxPitch");

                float min = minProp.floatValue;
                float max = maxProp.floatValue;

                EditorGUI.BeginChangeCheck();
                min = EditorGUILayout.FloatField(min, GUILayout.Width(40));
                EditorGUILayout.MinMaxSlider(ref min, ref max, -3f, 3f);
                max = EditorGUILayout.FloatField(max, GUILayout.Width(40));

                if (EditorGUI.EndChangeCheck())
                {
                    minProp.floatValue = min;
                    maxProp.floatValue = max;
                }
                EditorGUILayout.EndHorizontal();
            }
            else
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty("pitch"));
            }

            serializedObject.ApplyModifiedProperties();

            EditorGUILayout.Space(10);

            // ==========================================================
            // 💡 미리듣기(Preview) 버튼 UI 추가
            // ==========================================================
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("▶ Play Preview", GUILayout.Height(30)))
            {
                PlayPreview();
            }

            if (GUILayout.Button("■ Stop", GUILayout.Height(30)))
            {
                StopPreview();
            }
            EditorGUILayout.EndHorizontal();
        }

        private void PlayPreview()
        {
            if (data.audioClip == null)
            {
                Debug.LogWarning("AudioClip이 비어있습니다.");
                return;
            }

            StopPreview(); // 기존 재생 중인 소리 정지

            // 하이어라키에 보이지 않는 임시 오브젝트 생성
            GameObject previewGO = EditorUtility.CreateGameObjectWithHideFlags("AudioPreview", HideFlags.HideAndDontSave, typeof(AudioSource));
            previewSource = previewGO.GetComponent<AudioSource>();

            previewSource.clip = data.audioClip;
            previewSource.volume = data.volume;
            previewSource.spatialBlend = 0f; // 2D 사운드로 고정
            previewSource.playOnAwake = false;

            // 피치 적용
            if (data.isRandomPitch)
            {
                float randomPitch = UnityEngine.Random.Range(data.minPitch, data.maxPitch);
                previewSource.pitch = randomPitch;
                Debug.Log($"[Preview] 적용된 랜덤 피치: {randomPitch}");
            }
            else
            {
                previewSource.pitch = data.pitch;
            }

            previewSource.Play();
        }

        private void StopPreview()
        {
            if (previewSource != null)
            {
                previewSource.Stop();
                DestroyImmediate(previewSource.gameObject);
                previewSource = null;
            }
        }
    }
#endif
}