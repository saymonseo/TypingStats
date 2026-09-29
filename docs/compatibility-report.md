# Проверка совместимости: TypingStats 0.1.0

Дата: 29 сентября 2026 года. Это фактический отчёт о выполненных проверках, не рекламная матрица универсальной поддержки.

| Сценарий | Статус | Основание |
| --- | --- | --- |
| Сборка Core/Storage/App | passed | dotnet build Release, 0 warnings / 0 errors |
| Unicode-графемы и разные границы событий | passed | Console tests, включая составной emoji и суррогатные пары |
| Backspace / paste / navigation / signed net | passed | Core tests |
| UTC-минуты и активное время | passed for tested cases | Core tests; длительные смены timezone отдельно не проверены |
| SQLite idempotence / rollups / backup / restore / CSV | passed | Storage integration test |
| Нативное преобразование обычной раскладки + actor + SQLite | passed | Controlled metadata pipeline, 3 estimated + 2 observed = 5; net=4; paste=2; поздний payload после paste не вошёл в gross |
| Установка hook и старт COM/UIA | passed startup only | Smoke report; не означает подтверждение реальных клавиш |
| Горячая клавиша Ctrl+Alt+F12 | passed registration | Smoke report; физическое нажатие отдельно не проверено |
| Отрисовка основного окна | passed | DrawToBitmap и визуальная проверка; исправлены DPI-клиппинг и toolbar |
| Клики через Windows Computer Use | not completed | Ожидание доступа к тестовому окну завершилось timeout |
| Реальный набор RU/EN в Блокноте/Telegram/VS Code/браузере | not tested | Нужна ручная проверка с TestHost / реальными полями |
| Реальный японский/китайский/корейский IME | not tested | Внутренний тест числовой композиции не проверяет провайдер |
| Emoji-панель / software keyboard / injected provenance | not supported as guaranteed input | Адаптер происхождения не реализован |
| Полноценные dead keys | partial | Фиксация unknown, без обещания точной layout-композиции |
| Повышенные процессы / защищённые поля | not tested | Ограничения доступа не обходятся |
| 8 часов непрерывной работы / disk full / аварийный kill | not tested | Короткий запуск и тесты хранилища этого не доказывают |

Во всех непроверенных обычных полях объём по клавишам помечен как estimated. UIA TextEdit поддерживается активным элементом не всегда. Наличие подписки на pattern не означает, что конкретный путь IME прошёл приёмку.

Точное выполнение всего исходного ТЗ ещё не заявлено. Следующий инженерный этап — физическая проверка основных приложений и профилей ввода, после чего расширяется adapter/profile matrix.
