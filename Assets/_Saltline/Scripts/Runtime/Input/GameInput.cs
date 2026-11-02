using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Saltline.Input
{
    /// <summary>
    /// All gameplay input actions, defined in code so every binding is visible and rebindable.
    /// Keyboard/mouse first; gamepad bindings are included as a convenience.
    /// Rebinds are saved as JSON overrides in PlayerPrefs.
    /// </summary>
    public sealed class GameInput
    {
        const string PrefsKey = "saltline.bindings.v1";
        static GameInput s_Instance;
        public static GameInput Instance => s_Instance ??= new GameInput();

        public readonly InputActionAsset Asset;
        public readonly InputActionMap Gameplay;
        public readonly InputAction Move, Look, LookStick, Jump, Crouch, Sprint, Fire, Aim, Reload,
            Primary, Secondary, SwapWeapon, ToggleView, ShoulderSwap, Scoreboard, Pause;

        GameInput()
        {
            Asset = ScriptableObject.CreateInstance<InputActionAsset>();
            Asset.name = "SaltlineInput";
            Gameplay = Asset.AddActionMap("Gameplay");

            Move = Gameplay.AddAction("Move", InputActionType.Value, expectedControlLayout: "Vector2");
            Move.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/w").With("Down", "<Keyboard>/s")
                .With("Left", "<Keyboard>/a").With("Right", "<Keyboard>/d");
            Move.AddBinding("<Gamepad>/leftStick");

            Look = Gameplay.AddAction("Look", InputActionType.PassThrough, "<Mouse>/delta", expectedControlLayout: "Vector2");
            LookStick = Gameplay.AddAction("LookStick", InputActionType.Value, "<Gamepad>/rightStick", expectedControlLayout: "Vector2");

            Jump = Button("Jump", "<Keyboard>/space", "<Gamepad>/buttonSouth");
            Crouch = Button("Crouch", "<Keyboard>/leftCtrl", "<Keyboard>/c", "<Gamepad>/buttonEast");
            Sprint = Button("Sprint", "<Keyboard>/leftShift", "<Gamepad>/leftStickPress");
            Fire = Button("Fire", "<Mouse>/leftButton", "<Gamepad>/rightTrigger");
            Aim = Button("Aim", "<Mouse>/rightButton", "<Gamepad>/leftTrigger");
            Reload = Button("Reload", "<Keyboard>/r", "<Gamepad>/buttonWest");
            Primary = Button("Primary", "<Keyboard>/1");
            Secondary = Button("Secondary", "<Keyboard>/2");
            SwapWeapon = Button("SwapWeapon", "<Keyboard>/q", "<Gamepad>/buttonNorth");
            ToggleView = Button("ToggleView", "<Keyboard>/v", "<Gamepad>/dpad/up");
            ShoulderSwap = Button("ShoulderSwap", "<Keyboard>/x", "<Mouse>/middleButton", "<Gamepad>/rightStickPress");
            Scoreboard = Button("Scoreboard", "<Keyboard>/tab", "<Gamepad>/select");
            Pause = Button("Pause", "<Keyboard>/escape", "<Gamepad>/start");

            LoadBindings();
            Asset.Enable();
        }

        InputAction Button(string name, params string[] paths)
        {
            var a = Gameplay.AddAction(name, InputActionType.Button);
            foreach (var p in paths) a.AddBinding(p);
            return a;
        }

        public void LoadBindings()
        {
            string json = PlayerPrefs.GetString(PrefsKey, "");
            if (string.IsNullOrEmpty(json)) return;
            try { Asset.LoadBindingOverridesFromJson(json); }
            catch (Exception e) { Debug.LogWarning($"[SALTLINE] Ignoring saved bindings: {e.Message}"); }
        }

        public void SaveBindings()
        {
            PlayerPrefs.SetString(PrefsKey, Asset.SaveBindingOverridesAsJson());
            PlayerPrefs.Save();
        }

        public void ResetBindings()
        {
            Asset.RemoveAllBindingOverrides();
            PlayerPrefs.DeleteKey(PrefsKey);
        }

        /// <summary>Interactive rebind of one binding (Esc cancels). Mouse movement is ignored.</summary>
        public InputActionRebindingExtensions.RebindingOperation StartRebind(InputAction action, int bindingIndex, Action<bool> done)
        {
            action.Disable();
            var op = action.PerformInteractiveRebinding(bindingIndex)
                .WithControlsExcluding("<Mouse>/position")
                .WithControlsExcluding("<Mouse>/delta")
                .WithCancelingThrough("<Keyboard>/escape")
                .OnMatchWaitForAnother(0.1f);
            op.OnComplete(o => { o.Dispose(); action.Enable(); SaveBindings(); done?.Invoke(true); });
            op.OnCancel(o => { o.Dispose(); action.Enable(); done?.Invoke(false); });
            return op.Start();
        }
    }
}
