#include "pch.h"
#include "MainWidgetSettings.h"
#if __has_include("MainWidgetSettings.g.cpp")
#include "MainWidgetSettings.g.cpp"
#endif

using namespace winrt;
using namespace Windows::UI::Xaml;

namespace winrt::XboxGamingBar::implementation
{
    MainWidgetSettings::MainWidgetSettings()
    {
        InitializeComponent();
    }

    void MainWidgetSettings::ClickHandler(IInspectable const&, RoutedEventArgs const&)
    {
        Button().Content(box_value(L"Clicked"));
    }
}
