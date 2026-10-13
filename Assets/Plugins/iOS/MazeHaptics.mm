// Light Taptic Engine taps for Maze Swarm's UI (called from Maze.Haptics in Holo.cs).
// style 0..2 = light / medium / heavy impact, 10..12 = success / warning / error notification.
#import <UIKit/UIKit.h>

extern "C" void MazeHapticImpact(int style)
{
    if (style >= 10)
    {
        UINotificationFeedbackGenerator *n = [[UINotificationFeedbackGenerator alloc] init];
        [n prepare];
        [n notificationOccurred:(UINotificationFeedbackType)(style - 10)];
        return;
    }
    UIImpactFeedbackStyle s = style <= 0 ? UIImpactFeedbackStyleLight : style == 1 ? UIImpactFeedbackStyleMedium : UIImpactFeedbackStyleHeavy;
    UIImpactFeedbackGenerator *g = [[UIImpactFeedbackGenerator alloc] initWithStyle:s];
    [g prepare];
    [g impactOccurred];
}
