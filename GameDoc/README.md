# Игровая документация New_SRG

Начинайте с этого оглавления. Файлы, перечисленные в таблице актуальных досье, являются текущими редакциями; суффикс версии в имени не заменяет этот список. Историю текущих файлов хранит Git.

## Карта документации

| Раздел | Назначение и статус |
|---|---|
| [Общие справочники](#общие-справочники) | Канон физики, хронологии, миров и институтов |
| [Досье рас](#актуальные-досье-рас) | Текущие статьи о расах |
| `personage_*` | Досье персонажей; форма — в `templates/` |
| [Игровые условности](common_igrovye_uslovnosti.txt) | Упрощения игрового представления; не переписывают канон |
| [Шаблоны](templates/README.md) | Структура и правила оформления статей |
| [Редакционные материалы](editorial/README.md) | Планы, аудит, вопросы, журналы; не самостоятельный источник принятого канона |
| [Концепты](concepts/README.md) | Предложения; не канон до явного принятия автором |
| [Архив](archive/README.md) | Старые версии; не использовать как актуальные источники |
| [Геймдизайн](../docs/design/) | Спецификации игровых механик |
| [Техническая документация](../docs/modules/README.md) | Архитектура и реализация |

## Приоритет источников

1. Явные решения автора, зафиксированные в журнале решений [плана](editorial/PLAN_migracii_ras_v3.md#9-журнал-решений-автора).
2. `common_spravochnik_sistem.txt` — численные параметры звёзд и планет.
3. Общие справочники — каждый в своей предметной области: история, физика, институты и т. д.
4. Принятые реестры контактов и эпох — после их создания и проверки. Сейчас такие реестры не созданы и не могут служить источником.
5. Досье расы, которой принадлежит подробно раскрываемый факт.
6. Остальные актуальные досье.

Приоритет не означает право автоматически исправлять сомнительный факт: если принадлежность факта неясна или источники одного уровня расходятся, записать обе версии и вопрос автору. Большинство повторений не является доказательством. Архив, концепты и непринятые предложения не разрешают противоречия канона.

## Общие справочники

- [common_gipersfera.txt](common_gipersfera.txt)
- [common_history_extended.txt](common_history_extended.txt)
- [common_igrovye_uslovnosti.txt](common_igrovye_uslovnosti.txt)
- [common_instituty_sodruzhestva.txt](common_instituty_sodruzhestva.txt)
- [common_spravochnik_sistem.txt](common_spravochnik_sistem.txt)
- [common_tovary_i_kontrabanda.txt](common_tovary_i_kontrabanda.txt)
- [common_uchenie_uzora.txt](common_uchenie_uzora.txt)
- [common_vneshniy_rukav.txt](common_vneshniy_rukav.txt)

Отдельный планетологический объект без разумной расы: [ПХ-2114 «Расселина»](planetologiya_perhloratnaya_biosfera.txt).

## Актуальные досье рас

| Статья | Файл |
|---|---|
| АЭТАРИ | [also_races_1_aetari_v3.txt](also_races_1_aetari_v3.txt) |
| СПАСИТЕЛИ (РОЙ) | [also_races_2_spasiteli_v3.txt](also_races_2_spasiteli_v3.txt) |
| ВЕНТАРЫ | [also_races_3_ventary_v3.txt](also_races_3_ventary_v3.txt) |
| ГОРАНДЫ | [also_races_4_gorandy_v3.txt](also_races_4_gorandy_v3.txt) |
| КАЙРЕНЫ | [also_races_4_kayreny_v2.txt](also_races_4_kayreny_v2.txt) |
| ИИ ДРЕВНИХ | [also_races_5_ii_drevnih_v2.txt](also_races_5_ii_drevnih_v2.txt) |
| НАЭРЫ («СТРОИТЕЛИ») | [also_races_outer_stroiteli_v4.txt](also_races_outer_stroiteli_v4.txt) |
| ГРОНДАРЫ (БРОНОКСЫ) | [big_race_1_grondary_v4.txt](big_race_1_grondary_v4.txt) |
| КСЕНОПОДЫ | [big_race_2_ksenopody_v3.txt](big_race_2_ksenopody_v3.txt) |
| HOMO SYNTHETICUS (ЛЮДИ) | [big_race_3_homo_syntheticus_v3.txt](big_race_3_homo_syntheticus_v3.txt) |
| СИНАПТЫ | [big_race_4_sinapty_v3.txt](big_race_4_sinapty_v3.txt) |
| ЭТЕРИАНЕ | [big_race_5_etheriane_v3.txt](big_race_5_etheriane_v3.txt) |
| ТАРНАИ (ПРОПОВЕДНИКИ) | [big_race_6_propovedniki_v3.txt](big_race_6_propovedniki_v3.txt) |
| ВОРН | [small_race_1_vorn_v3.txt](small_race_1_vorn_v3.txt) |
| ХАМАРИ | [small_race_2_hamari_v3.txt](small_race_2_hamari_v3.txt) |
| АЭЛИТЫ | [small_race_3_aelity_v3.txt](small_race_3_aelity_v3.txt) |
| ДЕЙМИ | [small_race_4_deymi_v3.txt](small_race_4_deymi_v3.txt) |
| ВРАВШИ | [small_race_5_vravshi_v3.txt](small_race_5_vravshi_v3.txt) |
| ЗАРГАШИ | [small_race_6_zargashi_v3.txt](small_race_6_zargashi_v3.txt) |

Игровое приложение ИИ Древних: [also_races_5_ii_drevnih_gameplay_v1.txt](also_races_5_ii_drevnih_gameplay_v1.txt). Это дополнение, а не отдельная раса и не замена основному досье.

## Правила сопровождения

- Основной шаблон: [статья о расе v3](templates/Шаблон_статьи_о_расе_v3.md). Нумерация разделов фиксирована; для неприменимых тем используется явное или диапазонное закрытие.
- Полное описание факта хранится в одном профильном разделе, в остальных местах — адресные ссылки.
- Существующие классы рас сохранены. При необходимости технологический статус, членство, существование и игровую роль уточняют отдельно; изменение классификации требует содержательного согласования.
- При переносе файлов обновляются явные пути и Markdown-ссылки. Простое упоминание старого имени в историческом журнале не делает его актуальным источником.
- Проверка структуры: `python tools/docs/check_lore_structure.py` из корня репозитория. Она проверяет оформление, разделы, локальные ссылки и актуальный список; не подтверждает фактическую непротиворечивость канона.
