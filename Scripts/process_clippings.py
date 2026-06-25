import os
import sys
import re
import json
import shutil
import logging
import time
import argparse
from datetime import datetime
from dotenv import load_dotenv
from google import genai
from google.genai import types

# Load environment variables from .env if present
load_dotenv()

def clean_metadata_value(val):
    if not val:
        return ""
    if not isinstance(val, str):
        val = str(val)
    # Replace colons with dashes to avoid frontmatter parsing issues
    return val.replace(':', '-').strip()

# Setup paths
script_dir = os.path.dirname(os.path.abspath(__file__))

# Setup logging
bin_dir = None
current = script_dir
for _ in range(5):
    potential_bin = os.path.join(current, "bin")
    if os.path.exists(potential_bin) and os.path.isdir(potential_bin):
        bin_dir = potential_bin
        break
    current = os.path.dirname(current)

if bin_dir:
    logs_dir = os.path.join(bin_dir, "logs")
else:
    logs_dir = os.path.join(script_dir, "bin", "logs")

os.makedirs(logs_dir, exist_ok=True)
log_file_path = os.path.join(logs_dir, "process_clippings.log")

logging.basicConfig(
    level=logging.INFO,
    format='%(asctime)s - %(levelname)s - %(message)s',
    handlers=[
        logging.FileHandler(log_file_path, encoding='utf-8'),
        logging.StreamHandler(sys.stdout)
    ]
)
logger = logging.getLogger(__name__)

# Configure Gemini API
API_KEY = os.getenv("GEMINI_API_KEY", "")
MODEL_NAME = os.getenv("GEMINI_MODEL_NAME", "gemma-4-26b-a4b-it")

def sanitize_filename(name):
    # Keep alphanumeric, spaces, and hyphens/underscores/dots
    invalid_chars = r'[<>:"/\\|?*]'
    sanitized = re.sub(invalid_chars, '', name)
    return sanitized.strip()

def clean_and_parse_json(text):
    text = text.strip()
    
    first_brace = text.find('{')
    last_brace = text.rfind('}')
    
    if first_brace >= 0 and last_brace > first_brace:
        try:
            return json.loads(text[first_brace:last_brace+1])
        except Exception:
            pass

    if text.startswith("```json"):
        text = text[len("```json"):]
    elif text.startswith("```"):
        text = text[len("```"):]
    if text.endswith("```"):
        text = text[:-3]
    text = text.strip()
    return json.loads(text)

def main():
    parser = argparse.ArgumentParser(description="Process Clippings OCR Pipeline")
    parser.add_argument("--type", type=str, default="", help="The type metadata value to add to frontmatter")
    args = parser.parse_args()
    cli_type = args.type

    logger.info("Starting clippings processing pipeline...")

    # Validate API key
    if not API_KEY:
        logger.error("GEMINI_API_KEY environment variable is not set. Exiting.")
        sys.exit(1)

    # Instantiate Gemini Client
    try:
        client = genai.Client(api_key=API_KEY)
    except Exception as e:
        logger.error(f"Failed to initialize GenAI Client: {e}")
        sys.exit(1)

    # Define temp.json path
    temp_json_path = os.path.join(script_dir, "temp.json")
    if not os.path.exists(temp_json_path):
        logger.warning(f"temp.json not found at {temp_json_path}. Nothing to process.")
        sys.exit(0)

    # Read temp.json
    try:
        with open(temp_json_path, 'r', encoding='utf-8') as f:
            entries = json.load(f)
    except Exception as e:
        logger.error(f"Failed to read or parse {temp_json_path}: {e}")
        sys.exit(1)

    if not isinstance(entries, list) or not entries:
        logger.warning("temp.json is empty or not in the expected array format. Exiting.")
        sys.exit(0)

    logger.info(f"Found {len(entries)} entries to process in temp.json.")

    # Define prompt.md path
    prompt_path = os.path.join(script_dir, "prompt.md")
    if not os.path.exists(prompt_path):
        logger.error(f"System prompt file not found at {prompt_path}. Exiting.")
        sys.exit(1)

    try:
        with open(prompt_path, 'r', encoding='utf-8') as f:
            system_instruction = f.read()
    except Exception as e:
        logger.error(f"Failed to read prompt.md: {e}")
        sys.exit(1)

    # Initialize index.md path (temporary session index)
    index_path = os.path.join(script_dir, "index.md")
    try:
        with open(index_path, 'w', encoding='utf-8') as f:
            pass  # Start fresh and empty
        logger.info(f"Initialized temporary index.md at {index_path}.")
    except Exception as e:
        logger.error(f"Failed to create temporary index.md: {e}")
        sys.exit(1)

    clippings_dir = r"E:\ghost\documents\argus\04-raw\clippings"
    custom_metadata = []
    settings_path = os.path.join(script_dir, "settings.json")
    if os.path.exists(settings_path):
        try:
            with open(settings_path, 'r', encoding='utf-8') as sf:
                settings_data = json.load(sf)
                clippings_dir = settings_data.get("ClippingsSavePath", clippings_dir)
                custom_metadata = settings_data.get("CustomMetadata", [])
        except Exception as e:
            logger.error(f"Failed to load settings.json: {e}")

    os.makedirs(clippings_dir, exist_ok=True)

    success_count = 0
    total_entries = len(entries)

    for idx, entry in enumerate(entries, 1):
        logger.info(f"[{idx}/{total_entries}] Processing entry...")

        # Extract values case-insensitively
        timestamp = entry.get('Timestamp') or entry.get('timestamp', '')
        entry_type = entry.get('Type') or entry.get('type', 'text')
        text = entry.get('Text') or entry.get('text', '')

        if not text.strip():
            logger.info(f"[{idx}/{total_entries}] Entry has empty text. Skipping.")
            continue

        # Read current index.md
        try:
            with open(index_path, 'r', encoding='utf-8') as f:
                index_content = f.read()
        except Exception as e:
            logger.error(f"Failed to read index.md: {e}")
            index_content = ""

        # Construct user prompt
        user_prompt = f"""Here is the current running index of files created so far in this session (available for internal links):
---
{index_content}
---

Here is the raw OCR data you need to process:
---
Timestamp: {timestamp}
Type: {entry_type}
Text:
{text}
---"""

        # Query AI
        try:
            logger.info(f"  -> Sending request to Gemini using model '{MODEL_NAME}'...")
            response = client.models.generate_content(
                model=MODEL_NAME,
                contents=user_prompt,
                config=types.GenerateContentConfig(
                    system_instruction=system_instruction,
                    response_mime_type="application/json",
                )
            )

            ai_data = clean_and_parse_json(response.text)
        except Exception as e:
            logger.error(f"  -> ERROR processing entry: {e}")
            continue

        title = ai_data.get("title", f"Captured Note {idx}")
        description = ai_data.get("description", "")
        content = ai_data.get("content", "")
        internal_links = ai_data.get("internal_links", [])
        external_links = ai_data.get("external_links", [])

        # Clean metadata values to remove any colons
        type_clean = clean_metadata_value(cli_type)
        description_clean = clean_metadata_value(description)

        # Construct custom metadata lines
        custom_meta_str = ""
        for meta in custom_metadata:
            meta_key = meta.get("Key", "")
            meta_val = meta.get("Value", "")
            if meta_key:
                custom_meta_str += f"{clean_metadata_value(meta_key)}: {clean_metadata_value(meta_val)}\n"

        # Format links lists
        cleaned_internal_links = [link.replace(':', '') for link in internal_links] if internal_links else []
        internal_links_str = "\n".join(f"- {link}" for link in cleaned_internal_links) if cleaned_internal_links else ""
        external_links_str = "\n".join(f"- {ref}" for ref in external_links) if external_links else ""

        # Construct markdown file output
        md_content = f"""---
categories: Clippings
type: {type_clean}
description: {description_clean}
{custom_meta_str.strip()}
---

## Concept
{content}

## Links
{internal_links_str}

## References
{external_links_str}
"""

        # Save note file
        safe_title = sanitize_filename(title)
        note_filename = f"{safe_title}.md"
        note_filepath = os.path.join(clippings_dir, note_filename)

        try:
            with open(note_filepath, 'w', encoding='utf-8') as f:
                f.write(md_content)
            logger.info(f"  -> Saved note: {note_filename}")
            
            # Update running index
            with open(index_path, 'a', encoding='utf-8') as f:
                f.write(f"- [[{title}]] - {description_clean}\n")
            
            success_count += 1
        except Exception as e:
            logger.error(f"  -> Failed to write note {note_filename} or update index: {e}")

        # Sleep briefly between calls to be a good API citizen
        if idx < total_entries:
            time.sleep(10)

    # Save permanent index file to clippings folder before deleting temporary index.md
    if os.path.exists(index_path):
        try:
            safe_type = cli_type if cli_type else "General"
            index_filename = f"_index_{sanitize_filename(safe_type)}.md"
            permanent_index_path = os.path.join(clippings_dir, index_filename)
            
            with open(index_path, 'r', encoding='utf-8') as f:
                running_index_content = f.read()
                
            if running_index_content.strip():
                if os.path.exists(permanent_index_path):
                    with open(permanent_index_path, 'r', encoding='utf-8') as f:
                        existing = f.read()
                    if not (existing.endswith('\n') or existing.endswith('\r')):
                        existing += '\n'
                    with open(permanent_index_path, 'w', encoding='utf-8') as f:
                        f.write(existing + running_index_content)
                else:
                    header = f"# {safe_type} Index\n\n"
                    with open(permanent_index_path, 'w', encoding='utf-8') as f:
                        f.write(header + running_index_content)
                logger.info(f"Saved permanent index to: {permanent_index_path}")
        except Exception as e:
            logger.error(f"Failed to save permanent index file: {e}")

    # Cleanup temporary index.md
    if os.path.exists(index_path):
        try:
            os.remove(index_path)
            logger.info("Cleaned up temporary index.md.")
        except Exception as e:
            logger.warning(f"Failed to remove temporary index.md: {e}")

    # Relocate temp.json to backup folder only if all entries were successfully processed
    if success_count == total_entries:
        backup_dir = os.path.join(script_dir, "scrapped_backup")
        if not os.path.exists(backup_dir):
            # Look in parent directories
            parent = script_dir
            for _ in range(5):
                parent = os.path.dirname(parent)
                potential_backup = os.path.join(parent, "scrapped_backup")
                if os.path.exists(potential_backup):
                    backup_dir = potential_backup
                    break

        try:
            os.makedirs(backup_dir, exist_ok=True)
            timestamp_str = datetime.now().strftime("%Y%m%d_%H%M%S")
            backup_path = os.path.join(backup_dir, f"temp_{timestamp_str}.json")
            shutil.move(temp_json_path, backup_path)
            logger.info(f"Moved temp.json to backup: {backup_path}")
        except Exception as e:
            logger.error(f"Failed to backup temp.json: {e}")
    else:
        logger.info(f"Some or all entries failed to process ({success_count}/{total_entries} succeeded). Keeping temp.json in place.")

    logger.info(f"Completed processing batch: {success_count}/{total_entries} entries successfully processed.")

if __name__ == "__main__":
    main()
