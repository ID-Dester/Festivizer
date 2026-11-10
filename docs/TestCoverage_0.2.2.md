# Карта покрытия 0.2.2

Для текущего состояния работ 0.2.3 см. [промежуточную карту финализации](TestCoverage_0.2_Final.md); исторические результаты ниже сохранены.

Основание — §4.1, §4.2 и ручная матрица §5 [ТЗ 0.2.2](../doc/GarlandForWindows_0.2.2_MenuAndLifecycle_SPEC.md). `L` — логический прогон `logic-20261110-135235-745-a7aef1eb15fa4a6f8f7e4d7567a2f03e`, PASS 137. `D` — серия DesktopTests 10.11.2026: 5 Debug и 5 Release, см. [отчёт](ValidationReport_0.2.2.md). PASS на одном уровне не переносится на другой. Полная последовательность через host и ручная матрица отдельно не проходили.

| Требование | Сценарий и условия | Production-компонент | Уровень | Тест/протокол | Результат и run-id | Пробел/ограничение |
|---|---|---|---|---|---|---|
| M01 IDs | START/END с WINDOW/MENU/SYSMENU | WinEventRouter | политика | `ControllerScenarios` | PASS, L | Нет сквозной доставки hook → host |
| M02 popup | POPUPSTART/END | WinEventRouter | политика | `ControllerScenarios` | PASS, L | Реальные события hook не зафиксированы |
| M03 подменю | END при оставшемся меню | MenuStateTracker | адаптер с fake | флаг POPUPMENUMODE | PASS, L | Полной последовательности событий host нет |
| M04 повтор | повтор START, END без START | Router/host | политика | маршрутизация без счётчиков | ЧАСТИЧНО, L | Сами последовательности через host не проверены |
| M05 потерянный END | режим меню снят, очередная сверка | MenuStateTracker/FrameOverlayController | fake + контроллер | `real controller recovers without menu END` | PASS, L | Тайминг реального Dispatcher 500 мс не измерен |
| M06 старый A → B | поздний END A после перехода | Router/MenuStateTracker | fake | перечитывание контекста | ЧАСТИЧНО, L | Позднее событие через host не воспроизведено |
| M07 меню B | у новой цели активно меню | MenuStateTracker | fake | новый foreground, SYSTEMMENUMODE | PASS, L | Полный host A→B не проверен |
| M08 фоновое/трей/настройки | источники независимы | VisibilityPolicy/host | политика | `tray open`, `controller clears for own settings` | ЧАСТИЧНО, L | Комбинации и фоновые события через host не проверены |
| M09 ошибка API | Unknown → успешный повтор | MenuStateTracker | fake | `menu read failure`, `query recovers` | PASS, L | Реальный отказ Win32 не инъецирован |
| M10 выключение | меню закрылось при выключении | VisibilityPolicy/FrameOverlayController | fake + контроллер | `menu closing cannot override disabled setting` | PASS, L | Не проверено на полном приложении |
| F01 первая цель | показать/повторить/configure A | FrameOverlayController | контроллер + fake | `first show`, `reuses A` | PASS, L | Реальное изменение темы не проверено |
| F02 A→B | owner/PID/bounds и освобождение A | FrameOverlayController | контроллер + fake | `A to B` | PASS, L | Реальный A→B отдельно не выполнен в серии |
| F03 настройки | A → Clear → B | FrameOverlayController | контроллер + fake | `clears for own settings`, `restores B` | PASS, L | Окно настроек полного приложения не проверено |
| F04 старый DPI | DPI A в очереди → B | FrameOverlayController | контроллер + fake | `stale A DPI callback` | PASS, L | Настоящий DPI transition не выполнен |
| F05 shutdown | callback → Dispose → очередь | FrameOverlayController | контроллер + fake | `disposed controller ignores stale callback` | PASS, L | Реальная гонка Dispatcher не подтверждена |
| F06 уничтожение | target / overlay HWND | FrameOverlayController | fake + реальный Win32 | fake destroyed; Desktop target close | ЧАСТИЧНО, L/D | Отдельное реальное уничтожение overlay и следующая цель не проверены |
| F07 состояния | minimize/maximize/cloak/topmost/fullscreen/PID | WindowTracker/FrameOverlayController | политика + fake | Eligibility/invalid/fullscreen tests | ЧАСТИЧНО, L | Конкретные факты minimize, cloak и topmost через реальный Win32 не подтверждены |
| F08 отказы | создание/owner/styles/position/close | FrameOverlayController/OverlaySafety | fake | `creation`, `invalid owner`, `position`, `failed close` | PASS, L | Реальные отказы API не инъецированы |
| F09 retry | задержка A и новая B | FrameRetryGate/FrameOverlayController | контроллер + часы fake | `creation failure`, `failure A does not block B` | PASS, L | Не проверен тайминг на реальном desktop |
| F10 порядок | успешный API, неверный порядок | FrameOrder/FrameOverlayController | fake + контроллер | `wrong final order` | PASS, L | Реальный отказ коррекции не воспроизведён |
| H01 меню | Проводник и названный браузер, подменю | полное приложение | ручной GUI | §5 ТЗ | NOT_RUN | Desktop popup лишь собственное Win32-меню |
| H02 закрытие | Escape/вне/пункт | полное приложение | ручной GUI | §5 ТЗ | NOT_RUN | Нет подтверждения на кандидате |
| H03 настройки | 20 циклов A→настройки→A/B | полное приложение | ручной GUI | §5 ТЗ | NOT_RUN | Нет подтверждения на кандидате |
| H04 переходы | меню A → B и обратно | полное приложение | ручной GUI | §5 ТЗ | NOT_RUN | Нет подтверждения на кандидате |
| H05 трей | выключатели и Taskbar | полное приложение | ручной GUI | §5 ТЗ | NOT_RUN | Нет подтверждения на кандидате |
| H06 устойчивость | 100 переключений, move/resize/close | полное приложение | ручной GUI | §5 ТЗ | NOT_RUN | Нет подтверждения на кандидате |
| H07 DPI | DPI/разрешение/restored | полное приложение | ручной GUI | согласованный сеанс | NOT_RUN | Системные настройки не менялись |
| H08 выход | штатный выход | полное приложение | ручной GUI | §5 ТЗ | NOT_RUN | Экземпляр пользователя не завершался |

