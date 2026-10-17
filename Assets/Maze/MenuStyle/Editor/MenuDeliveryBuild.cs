#if UNITY_EDITOR
using System;using System.IO;using UnityEngine;using UnityEditor;using UnityEditor.Build;using UnityEditor.Build.Reporting;using UnityEditor.iOS.Xcode;
namespace Maze.EditorTools {
 public static class MenuDeliveryBuild {
  public static void BuildIOS(){
   string version=Environment.GetEnvironmentVariable("MENU_IOS_BUILD")??"20261014.1905",output=Environment.GetEnvironmentVariable("MENU_IOS_OUTPUT")??"Builds/MenuDeliveryIOS";
   string originalBuild=PlayerSettings.iOS.buildNumber,originalTeam=PlayerSettings.iOS.appleDeveloperTeamID;
   bool originalAutomatic=PlayerSettings.iOS.appleEnableAutomaticSigning;string bundle=PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.iOS);if(bundle!="com.mazeswarm.game")throw new InvalidOperationException("Unexpected bundle: "+bundle);
   try{PlayerSettings.iOS.buildNumber=version;PlayerSettings.iOS.appleDeveloperTeamID="NY5RKJK3FT";PlayerSettings.iOS.appleEnableAutomaticSigning=true;
    Debug.Log("MenuDeliveryBuild start: "+version+" bundle="+bundle+" team=NY5RKJK3FT UTC="+DateTime.UtcNow.ToString("O"));
    var report=BuildPipeline.BuildPlayer(new BuildPlayerOptions{scenes=new[]{"Assets/Maze/Scenes/Main.unity","Assets/Maze/Scenes/Sandbox.unity"},target=BuildTarget.iOS,locationPathName=output,options=BuildOptions.None});
    Debug.Log("MenuDeliveryBuild result="+report.summary.result+" errors="+report.summary.totalErrors+" size="+report.summary.totalSize);
    if(report.summary.result!=BuildResult.Succeeded)throw new InvalidOperationException("iOS build did not succeed");
    var plist=new PlistDocument();var path=output+"/Info.plist";plist.ReadFromFile(path);plist.root.SetBoolean("CADisableMinimumFrameDurationOnPhone",true);plist.WriteToFile(path);
   }finally{PlayerSettings.iOS.buildNumber=originalBuild;PlayerSettings.iOS.appleDeveloperTeamID=originalTeam;PlayerSettings.iOS.appleEnableAutomaticSigning=originalAutomatic;AssetDatabase.SaveAssets();}
  }
 }
}
#endif
