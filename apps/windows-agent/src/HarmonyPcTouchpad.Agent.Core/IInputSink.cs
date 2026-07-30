using HarmonyPcTouchpad.Agent.Protocol;

namespace HarmonyPcTouchpad.Agent.Core;

public interface IInputSink
{
    void MovePointer(PointerDeltaFrame frame);

    void SetButton(InputButton button, ButtonAction action);

    void Scroll(ScrollFrame frame);

    void HandleGesture(GestureFrame frame);

    void ReleaseAll();
}
