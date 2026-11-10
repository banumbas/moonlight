import os
import re
from deep_translator import GoogleTranslator
import time

variable_re = re.compile(r'(\{.*?\})')

def safe_translate_batch(texts):
    translator = GoogleTranslator(source='en', target='ru')
    batch_vars = []
    temp_texts = []
    for text in texts:
        vars_found = variable_re.findall(text)
        batch_vars.append(vars_found)
        temp_text = text
        for i, v in enumerate(vars_found):
            temp_text = temp_text.replace(v, f'<v{i}>')
        temp_texts.append(temp_text)

    try:
        translated_texts = translator.translate_batch(temp_texts)
    except Exception as e:
        print(f"Batch translation failed: {e}")
        return texts

    final_texts = []
    for i, (translated, vars_found) in enumerate(zip(translated_texts, batch_vars)):
        if not translated:
            final_texts.append(texts[i])
            continue

        for j, v in enumerate(vars_found):
            translated = translated.replace(f'<v{j}>', v)
            translated = translated.replace(f'< v{j} >', v)
            translated = translated.replace(f'< v{j}>', v)
            translated = translated.replace(f'<v{j} >', v)
        final_texts.append(translated)

    return final_texts

def process_ftl_file(filepath, out_filepath):
    # Create target directory if it doesn't exist
    os.makedirs(os.path.dirname(out_filepath), exist_ok=True)

    with open(filepath, 'r', encoding='utf-8') as f:
        content = f.read()

    lines = content.split('\n')

    translatable_lines_indices = []
    texts_to_translate = []

    for i, line in enumerate(lines):
        if not line.strip() or line.strip().startswith('#'):
            continue

        match = re.match(r'^(\s*[a-zA-Z0-9_-]+|\s*\.[a-zA-Z0-9_-]+)\s*=\s*(.*)', line)
        if match:
            val_part = match.group(2)
            if val_part.strip():
                translatable_lines_indices.append((i, match.group(1), 'key'))
                texts_to_translate.append(val_part)
        else:
            if line.startswith('    ') and not '=' in line:
                val_part = line.strip()
                if val_part:
                    translatable_lines_indices.append((i, None, 'multiline'))
                    texts_to_translate.append(val_part)

    if not texts_to_translate:
        # Just copy if nothing to translate
        with open(out_filepath, 'w', encoding='utf-8') as f:
            f.write('\n'.join(lines))
        return

    chunk_size = 30
    translated_texts = []
    for i in range(0, len(texts_to_translate), chunk_size):
        chunk = texts_to_translate[i:i+chunk_size]
        success = False
        retries = 3
        while not success and retries > 0:
            try:
                res = safe_translate_batch(chunk)
                translated_texts.extend(res)
                success = True
            except Exception as e:
                print(f"Error on chunk {i}, retrying...")
                retries -= 1
                time.sleep(2)
        if not success:
            print("Failed to translate chunk, keeping original")
            translated_texts.extend(chunk)
        time.sleep(0.3)

    for idx_info, translated_val in zip(translatable_lines_indices, translated_texts):
        line_idx = idx_info[0]
        if idx_info[2] == 'key':
            lines[line_idx] = f"{idx_info[1]} = {translated_val}"
        elif idx_info[2] == 'multiline':
            lines[line_idx] = f"    {translated_val}"

    with open(out_filepath, 'w', encoding='utf-8') as f:
        f.write('\n'.join(lines))

if __name__ == '__main__':
    dirs = [
        '_Afterlight', '_Carpmosia', '_CD', '_DEN', '_FarHorizons',
        '_Funkystation', '_Impstation', '_Moffstation', '_NullLink', '_Starlight'
    ]
    base_path = r'B:\builds\space-station-moonlight\Resources\Locale\en-US'
    target_base_path = r'B:\builds\space-station-moonlight\Resources\Locale\ru-RU'

    for d in dirs:
        d_path = os.path.join(base_path, d)
        if os.path.exists(d_path):
            for root, _, files in os.walk(d_path):
                for file in files:
                    if file.endswith('.ftl'):
                        file_path = os.path.join(root, file)
                        # calculate target path
                        rel_path = os.path.relpath(file_path, base_path)
                        target_file_path = os.path.join(target_base_path, rel_path)
                        print(f"Processing {rel_path}...")
                        process_ftl_file(file_path, target_file_path)
