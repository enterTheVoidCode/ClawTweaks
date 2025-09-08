#pragma once

#include "MainWidgetSettings.g.h"

namespace winrt::XboxGamingBar::implementation
{
    struct MainWidgetSettings : MainWidgetSettingsT<MainWidgetSettings>
    {
        MainWidgetSettings();

        void ClickHandler(Windows::Foundation::IInspectable const& sender, Windows::UI::Xaml::RoutedEventArgs const& args);
    };
}

namespace winrt::XboxGamingBar::factory_implementation
{
    struct MainWidgetSettings : MainWidgetSettingsT<MainWidgetSettings, implementation::MainWidgetSettings>
    {
    };
}
