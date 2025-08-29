#pragma once

#include "Widget1Settings.g.h"

namespace winrt::XboxGamingBar::implementation
{
    struct Widget1Settings : Widget1SettingsT<Widget1Settings>
    {
        Widget1Settings();

        void ClickHandler(Windows::Foundation::IInspectable const& sender, Windows::UI::Xaml::RoutedEventArgs const& args);
    };
}

namespace winrt::XboxGamingBar::factory_implementation
{
    struct Widget1Settings : Widget1SettingsT<Widget1Settings, implementation::Widget1Settings>
    {
    };
}
