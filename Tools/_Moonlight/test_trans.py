import os
import re
from deep_translator import GoogleTranslator

variable_re = re.compile(r'(\{.*?\})')

def safe_translate(text):
    translator = GoogleTranslator(source='en', target='ru')
    vars_found = variable_re.findall(text)
    temp_text = text
    for i, v in enumerate(vars_found):
        temp_text = temp_text.replace(v, f'<v{i}>')
    try:
        translated = translator.translate(temp_text)
    except Exception as e:
        print(f"Translation failed: {e}")
        return text
    if not translated:
        return text
    for i, v in enumerate(vars_found):
        translated = translated.replace(f'<v{i}>', v)
        translated = translated.replace(f'< v{i} >', v)
        translated = translated.replace(f'< v{i}>', v)
        translated = translated.replace(f'<v{i} >', v)
    return translated

def process_ftl(content):
    lines = content.split('\n')
    new_lines = []
    for line in lines:
        if not line.strip() or line.strip().startswith('#'):
            new_lines.append(line)
            continue
        match = re.match(r'^(\s*[a-zA-Z0-9_-]+|\s*\.[a-zA-Z0-9_-]+)\s*=\s*(.*)', line)
        if match:
            key_part = match.group(1)
            val_part = match.group(2)
            if val_part.strip():
                translated_val = safe_translate(val_part)
                new_lines.append(f"{key_part} = {translated_val}")
            else:
                new_lines.append(line)
        else:
            if line.startswith('    ') and not '=' in line:
                val_part = line.strip()
                if val_part:
                     translated_val = safe_translate(val_part)
                     new_lines.append(f"    {translated_val}")
                else:
                     new_lines.append(line)
            else:
                new_lines.append(line)
    return '\n'.join(new_lines)

if __name__ == '__main__':
    with open(r'B:\builds\space-station-moonlight\Resources\Locale\en-US\_Afterlight\floortiles.ftl', 'r', encoding='utf-8') as f:
        content = f.read()
    res = process_ftl(content)
    with open(r'B:\builds\space-station-moonlight\test_out.txt', 'w', encoding='utf-8') as f:
        f.write(res)
