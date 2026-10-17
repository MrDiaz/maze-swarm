using UnityEngine;using UnityEngine.EventSystems;
namespace Maze {
 // Keeps the existing HoloButton callback and selection contract with shared menu artwork.
 public sealed class MenuHoloButton:MonoBehaviour,IPointerDownHandler,IPointerUpHandler,IPointerExitHandler,IPointerClickHandler {
  HoloButton button;UnityEngine.UI.Image surface,selected;bool primary,pressed;
  public static void Apply(HoloButton b,bool yellow=false){if(b.GetComponent<MenuHoloButton>()!=null)return;b.enabled=false;var s=b.gameObject.AddComponent<MenuHoloButton>();s.button=b;s.primary=yellow;
   foreach(var image in new[]{b.fill,b.edge,b.brackets,b.glow,b.accent,b.tick,b.chevrons})if(image!=null)image.gameObject.SetActive(false);
   s.surface=Holo.Img(Holo.Fill(b.body,"MenuSurface"),MenuStyle.Art(yellow?"button_yellow_blank":"button_navy_blank"),Color.white,true,true);s.surface.transform.SetAsFirstSibling();s.selected=Holo.Img(Holo.Fill(b.body,"MenuSelection"),MenuStyle.Art("selected_outline"),Color.white,true);s.selected.raycastTarget=false;
   foreach(var sh in b.GetComponentsInChildren<UnityEngine.UI.Shadow>())Object.Destroy(sh);foreach(var tx in b.GetComponentsInChildren<UnityEngine.UI.Text>()){tx.color=yellow?MenuStyle.Navy:MenuStyle.Paper;}if(b.grid!=null)b.grid.gameObject.SetActive(false);if(b.sweep!=null)b.sweep.gameObject.SetActive(false);if(b.icon!=null)b.icon.color=yellow?MenuStyle.Navy:MenuStyle.Paper;
  }
  void LateUpdate(){if(button==null)return;surface.color=button.dimmed||!button.interactable?new Color(.52f,.57f,.66f):pressed?new Color(.79f,.85f,.93f):Color.white;selected.gameObject.SetActive(button.selected);if(button.label!=null)button.label.color=primary?MenuStyle.Navy:MenuStyle.Paper;button.body.localScale=Vector3.one;}
  public void OnPointerDown(PointerEventData e){pressed=true;}public void OnPointerUp(PointerEventData e){pressed=false;}public void OnPointerExit(PointerEventData e){pressed=false;}public void OnPointerClick(PointerEventData e){if(button.manual||!button.interactable)return;if(!button.silent)Sfx.Click();Haptics.Light();button.onClick?.Invoke();}void OnDisable(){pressed=false;}
 }
}
