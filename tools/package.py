from pathlib import Path
import json
import shutil
import xml.etree.ElementTree as ET
import zipfile
from datetime import datetime

project = Path(__file__).resolve().parents[1]
outputs = project / 'publish'
package = outputs / 'LANProof-Windows-0.1'
assert (package / 'LANProof.exe').is_file()
# The reports explicitly use system Arial. Preserve the unused bundled font
# outside the distributable instead of carrying it into every ZIP.
font = package / 'LatoFont'
if font.exists():
    preserved = project / 'work' / ('LatoFont-unused-' + datetime.now().strftime('%Y%m%d-%H%M%S-%f'))
    assert font.resolve().is_relative_to(project.resolve())
    assert preserved.resolve().is_relative_to(project.resolve())
    preserved.parent.mkdir(exist_ok=True)
    shutil.move(str(font), str(preserved))
shutil.copy2(project / 'README.md', package / 'README.md')
shutil.copy2(project / 'LICENSE', package / 'LICENSE')
shutil.copytree(project / 'assets', package / 'assets', dirs_exist_ok=True)
(package / 'START-HERE.txt').write_text('''LANProof 0.1 — приборный интерфейс

Запуск: LANProof.exe. Установка .NET не требуется.
Если используете ZIP, сначала распакуйте всю папку приложения.

Мониторинг начнётся при открытии окна. Кнопка «Приостановить»
останавливает сбор данных, после чего можно выполнить разовую проверку.
История, диагностика, отчёты и настройки доступны в боковом меню.
При закрытии окна мониторинг останавливается.

По умолчанию включена тёмная тема. Кнопка «Светлая тема» в верхней
панели переключает оформление без перезапуска. Выбор сохраняется.

Хранилище: папка data в корне проекта; вне проекта — рядом с exe.
История сохраняется при обновлении приложения.

Эта версия работает с Windows-ПК. Android-компаньон и синхронизация
между устройствами ещё не реализованы.
Подробнее — README.md. Проверки и ограничения — CHECKS.md.
''', encoding='utf-8-sig')
(package / 'CHECKS.md').write_text('''# Проверка LANProof 0.1

Дата: 21 октября 2026, Москва.

- Release-сборка решения: успешно, 0 ошибок и 0 предупреждений.
- Автоматические тесты: 49 / 49, без пропусков.
- Автономная Windows x64-сборка опубликована вместе с .NET runtime.
- Реальная сетевая проверка: пять результатов; код завершения 0; JSON, CSV и PDF созданы.
- Актуальный PDF проверен визуально; русские символы и таблица читаются.
- Предыдущий интерфейс: проверены запуск, остановка, история, выбор цикла, диагностика, настройки и открытие/отмена диалога экспорта. Фактическая запись через системный диалог автоматизацией не подтверждена; экспортёры проверены программно.
- Кнопка темы протестирована через событие интерфейса в обе стороны; выбор сохраняется и читается при следующем запуске. При отсутствии настроек используется тёмная тема.
- Все пять экранов автоматически отрисованы в двух темах через Avalonia Headless / Skia. Основные кадры проверены визуально; материалы сохранены в work/ui-preview проекта. Системные эффекты Acrylic/Mica в headless-проверку не входят.
- Обновление измерений сохраняет дерево страницы и читает только новые строки; проверено тестом интерфейса.
- Недельная синтетическая история: 604 800 записей, 1 413 точек превью, полная статистика. Точные времена последнего локального прогона — work/benchmarks/week-history.json; это измерение на данном ПК, не гарантия производительности.
- Проверены параллельные ICMP-цели, общий предел цикла, расписание запусков, повторное использование реального TCP-соединения HTTP и GET fallback при неподдерживаемом HEAD.
- Полные JSON/CSV проверены на 6 005 измерениях и 1 601 событии; отмена экспорта сохраняет предыдущий файл. Миграция старой базы проверена без потери измерений.
- Логотип и 11 SVG-иконок находятся в assets; ICO встроен в exe и окно. Загрузка встроенной иконки проверена тестом.
- Исходники, сборка, рабочие материалы и база перенесены в папку проекта. Выбор путей проекта и отдельного архива проверен тестами.

## Границы версии

Нет Android-компаньона, межустройственной синхронизации, фоновой службы и теста bufferbloat. Выводы являются диагностическими гипотезами. DNS может использовать системный кеш; ICMP может фильтроваться сетью. Уровень сигнала Wi-Fi Windows предоставляет не всегда.
''', encoding='utf-8')

assets = json.loads((project / 'src/LANProof.App/obj/project.assets.json').read_text(encoding='utf-8'))
roots = [Path(x) for x in assets['packageFolders']]
licenses = package / 'ThirdPartyLicenses'
licenses.mkdir(exist_ok=True)
notices = ['# Third-party components', '', 'Components keep their own licenses. Package metadata and available license files follow.', '']
mit = '''Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
THE SOFTWARE.
'''
for name, library in sorted(assets['libraries'].items()):
    if library['type'] != 'package':
        continue
    folder = next((root / library['path'] for root in roots if (root / library['path']).is_dir()), None)
    if folder is None:
        continue
    spec = next(folder.glob('*.nuspec'), None)
    if spec is None:
        continue
    metadata = ET.parse(spec).getroot().find('{*}metadata')
    def field(tag):
        node = metadata.find('{*}' + tag)
        return '' if node is None else (node.text or '')
    expression, copyright = field('license'), field('copyright')
    notices.extend([f'## {name}', f'Authors: {field("authors")}', f'License: {expression or field("licenseUrl")}', copyright, field('projectUrl'), ''])
    destination = licenses / name.replace('/', '-')
    destination.mkdir(exist_ok=True)
    if expression == 'MIT':
        (destination / 'MIT.txt').write_text((copyright + '\n\n' if copyright else '') + mit, encoding='utf-8')
    for path in folder.rglob('*'):
        if not path.is_file():
            continue
        filename = path.name.lower()
        if (filename.endswith(('.txt', '.md')) and ('license' in filename or 'third-party-notice' in filename)):
            target = destination / path.relative_to(folder)
            target.parent.mkdir(parents=True, exist_ok=True)
            shutil.copy2(path, target)
(package / 'THIRD-PARTY-NOTICES.md').write_text('\n'.join(notices), encoding='utf-8')
archive = outputs / 'LANProof-Liquid-Glass.zip'
with zipfile.ZipFile(archive, 'w', compression=zipfile.ZIP_DEFLATED, compresslevel=6) as bundle:
    for path in sorted(package.rglob('*')):
        if path.is_file():
            bundle.write(path, path.relative_to(outputs))
with zipfile.ZipFile(archive) as bundle:
    assert bundle.testzip() is None
    assert 'LANProof-Windows-0.1/LANProof.exe' in bundle.namelist()
print(f'Archive: {archive}\nSize: {archive.stat().st_size / 1024 / 1024:.1f} MB\nFiles: {len(list(package.rglob("*")))}')
