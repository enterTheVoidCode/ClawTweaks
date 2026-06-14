; =============================================================================
; ClawTweaks Hotkey Test / Live Key Logger (AutoHotkey v2)
;
; Opens a window that logs EVERY keyboard event live -- both keys you press and
; keys INJECTED by the ClawTweaks helper. Newest event is shown at the top.
;
; HOW TO USE
;   1. Run as ADMINISTRATOR (the ClawTweaks helper is elevated; a non-admin hook
;      may not see its injected events). Right-click the file -> "Run as
;      administrator", or from an elevated prompt:
;        & "C:\Program Files\AutoHotkey\v2\AutoHotkey64.exe" Test-Hotkeys.ahk
;   2. A window titled "ClawTweaks Key Logger" appears. Press a few keys on your
;      keyboard -- you should see them appear immediately. (If nothing shows up,
;      the script isn't running / wrong AHK version -- see notes at the bottom.)
;   3. Now trigger your ClawTweaks controller hotkey WITHOUT touching the
;      keyboard. Any keys that appear are being injected by ClawTweaks and ARE
;      reaching Windows. If nothing appears, the injection never reached the OS
;      (check the helper log line "Injected shortcut ... -> foreground: ...").
;   4. Optional: press F12 for the built-in Key History window. Its "Type" column
;      labels each event:  i = injected (ClawTweaks),  h = hardware (you).
;
;   Close the window to quit.
; =============================================================================

#Requires AutoHotkey v2.0
#SingleInstance Force
InstallKeybdHook                 ; force the keyboard hook so injected events are seen

LogGui := Gui("+AlwaysOnTop +Resize", "ClawTweaks Key Logger")
LogGui.SetFont("s10", "Consolas")
global LogCtrl := LogGui.Add("Edit", "w660 r26 ReadOnly")
LogGui.OnEvent("Close", (*) => ExitApp())
LogGui.Show()

AddLine("Ready. Press keys or trigger a hotkey - newest event appears at the top.")
AddLine("Keys appearing WITHOUT touching the keyboard = injected by ClawTweaks.")
AddLine("F12 = Key History window (Type column: i = injected, h = hardware).")
AddLine("----------------------------------------------------------------------")

AddLine(txt) {
    global LogCtrl
    LogCtrl.Value := txt "`r`n" LogCtrl.Value   ; prepend so newest stays visible
}

OnKey(*) {
    hk := A_ThisHotkey
    dir := InStr(hk, " Up") ? "UP  " : "DOWN"
    if RegExMatch(hk, "i)vk([0-9a-f]+)", &m) {
        name := GetKeyName("vk" m[1])
        if (name = "")
            name := "(unnamed)"
        AddLine(Format("{1}  {2}  VK=0x{3}  {4}", FormatTime(, "HH:mm:ss"), dir, StrUpper(m[1]), name))
    }
}

; Register a pass-through wildcard hotkey (down + up) for every virtual-key code,
; so we get a live callback for each key -- including injected ones.
loop 254 {
    code := A_Index
    if (code < 8 || code = 0x7B)     ; skip control range and F12 (reserved below)
        continue
    hex := Format("{:02X}", code)
    try Hotkey("~*vk" hex, OnKey)
    try Hotkey("~*vk" hex " Up", OnKey)
}

F12::KeyHistory
