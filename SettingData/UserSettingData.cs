using System;
using UnityEngine;

[Serializable]
public class UserSettingData
{
    public int mouseSensitivityIndex = 5;
    public int yInvertIndex = 0;

    public int windowModeIndex = 0;
    public int resolutionIndex = 0;

    public int savedResWidth = 0;
    public int savedResHeight = 0;
    public string savedMicName = "";

    public int masterVolumeIndex = 5;
    public int bgmVolumeIndex = 5;
    public int sfxVolumeIndex = 5;
    public int micDeviceIndex = 0;
    public int micModeIndex = 0;
    public KeyCode pushToTalkKey = KeyCode.T;
}