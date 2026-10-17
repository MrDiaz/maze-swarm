namespace Maze
{
    public partial class MainMenu
    {
        MenuPresentation menuPresentation;
        public MenuPresentation Presentation=>menuPresentation;
        public bool LegacySheetOpen=>current!=null;
        public bool MenuTitleEntered=>titleView!=null&&titleView.Entered;
        public bool TitleVisible=>titleView!=null&&titleView.Visible;
        public void ReturnToTitle()=>titleView?.ReturnToTitle();
        public void RefreshStyledLabels()=>RefreshLabels();
        public void ChooseStyledWeapon(int index){if(game.WeaponAllowed(index))SelectWeapon(index,true);}
    }
    public partial class MapSelect
    {
        public bool WaitingForMenu=>waitingLoadout&&!canvas.enabled;
        public void OpenStyledChange(bool hero)
        {
            if(mode!=Mode.Brief||menu.Presentation==null)return;
            waitingLoadout=true;canvas.enabled=false;menu.SetCanvasVisible(true);
            if(hero)menu.Presentation.OpenHeroes();else menu.Presentation.OpenWeapons();
        }
    }
}
