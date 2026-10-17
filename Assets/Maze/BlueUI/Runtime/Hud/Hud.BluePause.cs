using TMPro;
using UnityEngine;

namespace Maze
{
    public partial class Hud
    {
        UnityEngine.UI.Button blueSoundButton, blueHapticButton;
        RectTransform blueLeaveConfirm;
        TMP_Text blueSoundLabel, blueHapticLabel;

        // Pausing does not alter the run. Returning to the hangar remains a deliberate action.
        void BuildBluePause()
        {
            pauseRt = Dim(canvas.transform, "Pause", new Color(.09f,.23f,.48f,.26f), out pauseCg, out pauseScan);
            pauseScan.gameObject.SetActive(false);
            var frame = BlueUi.Frame(pauseRt,"PauseLayout");
            pausePanel = BlueUi.Panel(frame,"Panel",516,139,640,677,false,true).rectTransform;
            var title = BlueUi.Text(pausePanel,"Title","Paused",30,25,580,121,108);
            title.alignment = TextAlignmentOptions.Center;
            BlueUi.Button(pausePanel,"Resume","Resume",48,168,544,154,()=>game.TogglePause(),true,"icon_play",82);
            BlueUi.Button(pausePanel,"Hangar","Hangar",48,344,544,148,()=>blueLeaveConfirm.gameObject.SetActive(true),false,"icon_home",78);

            // The game has one master Sound setting. Its independent Haptics setting is retained;
            // the mock's music icon must not imply an unsupported separate music mixer.
            blueSoundButton = BlueUi.Button(pausePanel,"Sound","",152,524,148,123,()=>
            {
                Prefs.Sound = !Prefs.Sound;
                if (!FirstCryoAudio.Active(game)) Sfx.Toggle();
                RefreshBluePause();
            },false,"icon_sound");
            var soundIcon = blueSoundButton.transform.Find("Icon") as RectTransform;
            if (soundIcon != null) { soundIcon.anchoredPosition = new Vector2(38,-18); soundIcon.sizeDelta = new Vector2(72,64); }
            blueHapticButton = BlueUi.Button(pausePanel,"Haptics","",340,524,148,123,()=>
            {
                Prefs.HapticsOn = !Prefs.HapticsOn;
                Haptics.Medium(); RefreshBluePause();
            });
            var hapticName = BlueUi.Text(blueHapticButton.transform,"HapticName","Haptics",5,22,138,52,28,Color.white);
            hapticName.alignment = TextAlignmentOptions.Center;
            blueSoundLabel = BlueUi.Text(blueSoundButton.transform,"State","",0,84,148,26,22,Color.white,true);
            blueSoundLabel.alignment = TextAlignmentOptions.Center;
            blueHapticLabel = BlueUi.Text(blueHapticButton.transform,"State","",0,84,148,26,22,Color.white,true);
            blueHapticLabel.alignment = TextAlignmentOptions.Center;

            blueLeaveConfirm = BlueUi.Rect(frame,"LeaveConfirmation",0,0,BlueUi.Width,BlueUi.Height);
            BlueUi.Scrim(blueLeaveConfirm,"Shade",new Color(.03f,.1f,.25f,.42f));
            var confirm = BlueUi.Panel(blueLeaveConfirm,"Panel",436,270,800,400).rectTransform;
            var question = BlueUi.Text(confirm,"Title","Return to hangar?",38,26,724,74,60);
            question.alignment = TextAlignmentOptions.Center;
            var warning = BlueUi.Text(confirm,"Warning","The current run will end.",44,112,712,70,36,BlueUi.Muted,true);
            warning.alignment = TextAlignmentOptions.Center;
            BlueUi.Button(confirm,"Stay","Stay",38,230,344,112,()=>blueLeaveConfirm.gameObject.SetActive(false),true,null,48);
            BlueUi.Button(confirm,"Leave","Hangar",418,230,344,112,()=>game.ToMenu(),false,"icon_home",44);
            blueLeaveConfirm.gameObject.SetActive(false);
            RefreshBluePause();
        }

        void RefreshBluePause()
        {
            if (blueSoundButton == null) return;
            blueSoundLabel.text = Prefs.Sound ? "Sound on" : "Sound off";
            blueHapticLabel.text = Prefs.HapticsOn ? "On" : "Off";
            SetBlueToggle(blueSoundButton,Prefs.Sound);
            SetBlueToggle(blueHapticButton,Prefs.HapticsOn);
        }

        static void SetBlueToggle(UnityEngine.UI.Button button,bool on)
        {
            var tint = on ? Color.white : new Color(.68f,.78f,.9f,1);
            if (button.targetGraphic != null) button.targetGraphic.color = tint;
            var icon = button.transform.Find("Icon")?.GetComponent<UnityEngine.UI.Image>();
            if (icon != null) icon.color = tint;
            // The transparent hit target supplies the full mobile touch area, never visible ink.
            var touchTarget = button.transform.Find("TouchTarget")?.GetComponent<UnityEngine.UI.Image>();
            if (touchTarget != null) touchTarget.color = Color.clear;
        }

        void ResetBluePauseConfirmation()
        {
            if (blueLeaveConfirm != null) blueLeaveConfirm.gameObject.SetActive(false);
        }
    }
}
