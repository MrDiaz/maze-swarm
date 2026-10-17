using System;using System.Linq;using UnityEngine;
namespace Maze {
 public static class HeavyRoster {
  const string Version="ms.heavyRoster.v1",Backup="ms.heavyRoster.archive.v1";
  [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
  static void Load(){
   // The historical one-Hero migration is complete once its version is recorded.
   // A later selection (including Engineer) must never re-migrate progression or shards.
   if(PlayerPrefs.GetInt(Version,0)>=1)return;
   string selected=PlayerPrefs.GetString("ms.hero","vanguard"),original=PlayerPrefs.GetString("ms.progress","");
   if(!PlayerPrefs.HasKey(Backup))PlayerPrefs.SetString(Backup,original);
   var d=Progress.D;Migrate(d,selected);
   PlayerPrefs.SetString("ms.hero",Prefs.NormalizeHero(selected));PlayerPrefs.SetInt(Version,1);Progress.Save();PlayerPrefs.Save();
  }
  static int Value(System.Collections.Generic.List<string> rows,string key){var row=rows.FirstOrDefault(x=>x.StartsWith(key+":"));return row!=null&&int.TryParse(row.Substring(key.Length+1),out int n)?n:0;}
  static void Write(System.Collections.Generic.List<string> rows,string key,int n){rows.RemoveAll(x=>x.StartsWith(key+":"));rows.Add(key+":"+n);}
  // Old IDs exist only at this migration boundary; currencies, items and unlocks are untouched.
  public static void Migrate(SaveData d,string previous){
   string[] old={"sentinel","scout"};int lv=Math.Max(1,Value(d.heroLv,"vanguard")),stars=Math.Max(1,Value(d.heroStars,"vanguard")),shards=Value(d.shards,"vanguard");
   foreach(var id in old){lv=Math.Max(lv,Value(d.heroLv,id));stars=Math.Max(stars,Value(d.heroStars,id));shards+=Value(d.shards,id);}
   stars=Math.Max(stars,Math.Min(5,(lv+9)/10));
   if(previous!="vanguard")foreach(var row in d.talents.Where(x=>x.StartsWith(previous+".")).ToArray()){var moved="vanguard"+row.Substring(previous.Length);var key=moved.Substring(0,moved.LastIndexOf(':'));if(!d.talents.Any(x=>x.StartsWith(key+":")))d.talents.Add(moved);}
   foreach(var rows in new[]{d.heroLv,d.heroStars,d.shards})rows.RemoveAll(x=>old.Any(id=>x.StartsWith(id+":")));
   d.talents.RemoveAll(x=>old.Any(id=>x.StartsWith(id+".")));Write(d.heroLv,"vanguard",lv);Write(d.heroStars,"vanguard",stars);Write(d.shards,"vanguard",shards);
  }
 }
}
