import asyncio
import hashlib
import hmac
import json
import logging
import os
import sys
from typing import Optional, List, Dict, Any

import aiohttp
from aiohttp import web
import discord
from discord import app_commands
from discord.ext import commands

logging.basicConfig(
    level=logging.INFO,
    format="%(asctime)s [%(levelname)s] %(name)s: %(message)s",
    datefmt="%Y-%m-%d %H:%M:%S"
)
logger = logging.getLogger("DiscordLinkBot")

CONFIG_JSON_PATH = os.path.join(os.path.dirname(os.path.abspath(__file__)), "config.json")
EXAMPLE_CONFIG_PATH = os.path.join(os.path.dirname(os.path.abspath(__file__)), "config.example.json")

def parse_simple_toml(content: str) -> dict:
    """Fallback simple TOML parser for basic key-value and section structures."""
    result: Dict[str, Any] = {}
    current_section = result
    for raw_line in content.splitlines():
        line = raw_line.strip()
        if not line or line.startswith("#"):
            continue
        if line.startswith("[") and line.endswith("]"):
            section_name = line[1:-1].strip()
            parts = section_name.split(".")
            target = result
            for p in parts:
                if p not in target or not isinstance(target[p], dict):
                    target[p] = {}
                target = target[p]
            current_section = target
            continue
        if "=" in line:
            key, val = line.split("=", 1)
            key = key.strip()
            val = val.strip()
            if not (val.startswith('"') and val.endswith('"')) and "#" in val:
                val = val.split("#", 1)[0].strip()
            if val.startswith('"') and val.endswith('"'):
                parsed_val = val[1:-1].encode("utf-8").decode("unicode_escape", errors="ignore")
            elif val.startswith("'") and val.endswith("'"):
                parsed_val = val[1:-1]
            elif val.lower() == "true":
                parsed_val = True
            elif val.lower() == "false":
                parsed_val = False
            elif val.startswith("[") and val.endswith("]"):
                raw_items = val[1:-1].split(",")
                parsed_list = []
                for item in raw_items:
                    item = item.strip()
                    if not item:
                        continue
                    if item.startswith('"') and item.endswith('"'):
                        parsed_list.append(item[1:-1])
                    elif item.isdigit():
                        parsed_list.append(int(item))
                    else:
                        parsed_list.append(item)
                parsed_val = parsed_list
            else:
                try:
                    if "." in val:
                        parsed_val = float(val)
                    else:
                        parsed_val = int(val)
                except ValueError:
                    parsed_val = val
            current_section[key] = parsed_val
    return result

def read_toml_file(path: str) -> dict:
    try:
        import tomllib
        with open(path, "rb") as f:
            return tomllib.load(f)
    except ImportError:
        try:
            import tomli as tomllib
            with open(path, "rb") as f:
                return tomllib.load(f)
        except ImportError:
            with open(path, "r", encoding="utf-8") as f:
                return parse_simple_toml(f.read())

def find_config_path() -> Optional[str]:
    # 1. Check CLI arguments
    for i, arg in enumerate(sys.argv[1:]):
        if arg == "--config-file" and i + 1 < len(sys.argv[1:]):
            return sys.argv[1:][i + 1]
        if arg.endswith(".toml") or arg.endswith(".json"):
            if os.path.exists(arg):
                return arg

    # 2. Priority candidates
    candidates = [
        # In current working directory
        os.path.join(os.getcwd(), "server_config.toml"),
        # In repo root (two levels up from Tools/_Classic/DiscordBot)
        os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "server_config.toml"),
        # In bot folder
        os.path.join(os.path.dirname(os.path.abspath(__file__)), "server_config.toml"),
        # Traditional config.json
        CONFIG_JSON_PATH,
        os.path.join(os.getcwd(), "config.json"),
    ]
    for path in candidates:
        norm = os.path.normpath(path)
        if os.path.exists(norm):
            return norm
    return None

def load_config() -> dict:
    cfg_path = find_config_path()
    if not cfg_path:
        logger.error(
            "Neither 'server_config.toml' nor 'config.json' was found! "
            "Please ensure 'server_config.toml' is present in your server root directory or create 'config.json'."
        )
        sys.exit(1)

    logger.info(f"Loading configuration from: {cfg_path}")

    if cfg_path.endswith(".toml"):
        toml_data = read_toml_file(cfg_path)
        discord_sec = toml_data.get("discord", {})
        bot_sec = toml_data.get("discord_bot", {})
        admin_sec = toml_data.get("admin", {})
        status_sec = toml_data.get("status", {})
        web_sec = toml_data.get("web", {})

        return {
            "discord": {
                "bot_token": discord_sec.get("token") or bot_sec.get("bot_token") or bot_sec.get("token") or "",
                "client_id": str(discord_sec.get("api_key") or discord_sec.get("client_id") or bot_sec.get("client_id") or ""),
                "client_secret": discord_sec.get("client_secret") or bot_sec.get("client_secret") or "",
                "redirect_uri": discord_sec.get("callback") or discord_sec.get("redirect_uri") or bot_sec.get("redirect_uri") or "",
                "guild_id": int(discord_sec.get("guild_id") or bot_sec.get("guild_id") or 0),
                "admin_role_ids": bot_sec.get("admin_role_ids") or discord_sec.get("admin_role_ids") or []
            },
            "ss14_server": {
                "api_url": status_sec.get("connect_address") or "http://127.0.0.1:1212",
                "api_token": admin_sec.get("api_token") or "",
                "secret": discord_sec.get("secret") or bot_sec.get("secret") or ""
            },
            "web": {
                "host": bot_sec.get("host") or web_sec.get("host") or "0.0.0.0",
                "port": int(bot_sec.get("port") or web_sec.get("port") or 8080),
                "server_name": web_sec.get("server_name") or "Space Station Classic"
            }
        }
    else:
        with open(cfg_path, "r", encoding="utf-8") as f:
            return json.load(f)

CONFIG = load_config()

DISCORD_CFG = CONFIG.get("discord", {})
SS14_CFG = CONFIG.get("ss14_server", {})
WEB_CFG = CONFIG.get("web", {})

BOT_TOKEN = DISCORD_CFG.get("bot_token", "")
CLIENT_ID = str(DISCORD_CFG.get("client_id", ""))
CLIENT_SECRET = DISCORD_CFG.get("client_secret", "")
REDIRECT_URI = DISCORD_CFG.get("redirect_uri", "")
GUILD_ID = int(DISCORD_CFG.get("guild_id", 0))
ADMIN_ROLE_IDS = set(DISCORD_CFG.get("admin_role_ids", []))

SS14_API_URL = SS14_CFG.get("api_url", "http://127.0.0.1:1212").rstrip("/")
SS14_API_TOKEN = SS14_CFG.get("api_token", "")
SS14_SECRET = SS14_CFG.get("secret", "")

SERVER_NAME = WEB_CFG.get("server_name", "Space Station Classic")
WEB_HOST = WEB_CFG.get("host", "0.0.0.0")
WEB_PORT = int(WEB_CFG.get("port", 8080))



def verify_hmac_state(state: str, secret: str) -> Optional[str]:
    """
    Verifies that the OAuth state parameter was signed with our secret HMAC key.
    State format from SS14: '{customState}.{hmac_hex}'
    Returns the customState (NetUserId) if valid, None otherwise.
    """
    if not state or "." not in state:
        return None
    custom_state, received_hmac = state.rsplit(".", 1)
    expected_hmac = hmac.new(
        secret.encode("utf-8"),
        custom_state.encode("utf-8"),
        hashlib.sha256
    ).hexdigest().lower()

    if hmac.compare_digest(received_hmac.lower(), expected_hmac):
        return custom_state
    return None


def render_html(title: str, heading: str, message: str, is_success: bool = True, extra_info: str = "") -> str:
    """Renders a futuristic Space Station themed dark-mode HTML response."""
    theme_color = "#38ef7d" if is_success else "#ff4b2b"
    glow_color = "rgba(56, 239, 125, 0.3)" if is_success else "rgba(255, 75, 43, 0.3)"
    icon = "&#10004;" if is_success else "&#9888;"

    return f"""<!DOCTYPE html>
<html lang="ru">
<head>
    <meta charset="UTF-8">
    <meta name="viewport" content="width=device-width, initial-scale=1.0">
    <title>{title} — {SERVER_NAME}</title>
    <style>
        * {{
            margin: 0;
            padding: 0;
            box-sizing: border-box;
            font-family: -apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, Oxygen, Ubuntu, Cantarell, "Fira Sans", "Droid Sans", "Helvetica Neue", sans-serif;
        }}
        body {{
            background: #0d1117;
            background-image:
                radial-gradient(circle at 15% 15%, rgba(30, 58, 138, 0.25) 0%, transparent 40%),
                radial-gradient(circle at 85% 85%, rgba(88, 28, 135, 0.25) 0%, transparent 40%),
                linear-gradient(180deg, #0d1117 0%, #161b22 100%);
            color: #c9d1d9;
            display: flex;
            align-items: center;
            justify-content: center;
            min-height: 100vh;
            padding: 20px;
        }}
        .card {{
            background: rgba(22, 27, 34, 0.85);
            backdrop-filter: blur(12px);
            border: 1px solid rgba(255, 255, 255, 0.1);
            border-radius: 16px;
            box-shadow: 0 16px 40px rgba(0, 0, 0, 0.6), 0 0 30px {glow_color};
            max-width: 520px;
            width: 100%;
            padding: 40px 32px;
            text-align: center;
            animation: fadeIn 0.4s ease-out;
        }}
        @keyframes fadeIn {{
            from {{ opacity: 0; transform: translateY(12px); }}
            to {{ opacity: 1; transform: translateY(0); }}
        }}
        .icon-box {{
            width: 76px;
            height: 76px;
            border-radius: 50%;
            background: rgba(255, 255, 255, 0.05);
            border: 2px solid {theme_color};
            color: {theme_color};
            display: flex;
            align-items: center;
            justify-content: center;
            font-size: 38px;
            margin: 0 auto 24px;
            box-shadow: 0 0 20px {glow_color};
        }}
        h1 {{
            font-size: 24px;
            color: #f0f6fc;
            margin-bottom: 12px;
            font-weight: 700;
        }}
        p {{
            font-size: 15px;
            line-height: 1.6;
            color: #8b949e;
            margin-bottom: 24px;
        }}
        .details {{
            background: rgba(13, 17, 23, 0.7);
            border: 1px solid rgba(255, 255, 255, 0.08);
            border-radius: 8px;
            padding: 14px;
            margin-bottom: 24px;
            text-align: left;
            font-size: 13px;
            font-family: ui-monospace, SFMono-Regular, SF Mono, Menlo, Consolas, monospace;
            color: #58a6ff;
            word-break: break-all;
        }}
        .btn {{
            display: inline-block;
            background: {theme_color};
            color: #0d1117;
            font-weight: 600;
            font-size: 14px;
            padding: 12px 28px;
            border-radius: 8px;
            text-decoration: none;
            transition: all 0.2s ease;
            box-shadow: 0 4px 12px {glow_color};
        }}
        .btn:hover {{
            filter: brightness(1.15);
            transform: translateY(-1px);
        }}
        .footer {{
            margin-top: 28px;
            font-size: 12px;
            color: #484f58;
        }}
    </style>
</head>
<body>
    <div class="card">
        <div class="icon-box">{icon}</div>
        <h1>{heading}</h1>
        <p>{message}</p>
        {f'<div class="details">{extra_info}</div>' if extra_info else ''}
        <a href="javascript:window.close();" class="btn">Вернуться в игру</a>
        <div class="footer">{SERVER_NAME} &bull; Авторизация Discord</div>
    </div>
</body>
</html>"""


# ==============================================================================
# DISCORD BOT SETUP
# ==============================================================================

intents = discord.Intents.default()
intents.members = True
bot = commands.Bot(command_prefix="!", intents=intents)


def is_bot_admin(interaction: discord.Interaction) -> bool:
    """Checks if the user has Administrator permission or one of configured admin roles."""
    if interaction.user.guild_permissions.administrator:
        return True
    if isinstance(interaction.user, discord.Member) and ADMIN_ROLE_IDS:
        user_role_ids = {r.id for r in interaction.user.roles}
        if bool(user_role_ids & ADMIN_ROLE_IDS):
            return True
    return False


async def link_user_on_server(user_id_or_name: str, discord_id: int, roles: Optional[List[int]] = None) -> tuple[bool, str]:
    """Sends a link request to the SS14 server REST API."""
    url = f"{SS14_API_URL}/admin/actions/discord/link"
    headers = {
        "Authorization": f"SS14Token {SS14_API_TOKEN}",
        "Content-Type": "application/json",
        "Actor": json.dumps({"Guid": "00000000-0000-0000-0000-000000000000", "Name": "DiscordBot"})
    }
    payload: Dict[str, Any] = {
        "user": user_id_or_name,
        "discordId": discord_id,
    }
    if roles is not None:
        payload["roles"] = roles

    try:
        async with aiohttp.ClientSession() as session:
            async with session.post(url, headers=headers, json=payload, timeout=aiohttp.ClientTimeout(total=8)) as resp:
                if resp.status == 200:
                    return True, "Успешно привязано"
                text = await resp.text()
                try:
                    data = json.loads(text)
                    err = data.get("message") or data.get("error") or text
                except Exception:
                    err = text
                return False, f"Ошибка сервера ({resp.status}): {err}"
    except Exception as e:
        logger.error(f"Failed to connect to SS14 API at {url}: {e}")
        return False, f"Не удалось подключиться к серверу SS14: {e}"


@bot.event
async def on_ready():
    logger.info(f"Discord Bot logged in as {bot.user} (ID: {bot.user.id})")
    try:
        synced = await bot.tree.sync()
        logger.info(f"Synced {len(synced)} slash command(s).")
    except Exception as e:
        logger.error(f"Failed to sync slash commands: {e}")


@bot.tree.command(name="link", description="Привязать Discord ID к игроку SS14 (только для администрации)")
@app_commands.describe(player="Имя игрока или NetUserId (GUID)", user="Пользователь Discord (по умолчанию вызывающий)")
async def slash_link(interaction: discord.Interaction, player: str, user: Optional[discord.User] = None):
    if not is_bot_admin(interaction):
        await interaction.response.send_message("❌ У вас нет прав для выполнения этой команды.", ephemeral=True)
        return

    target_user = user or interaction.user
    roles: List[int] = []

    # If in a guild, collect member roles
    if interaction.guild:
        member = interaction.guild.get_member(target_user.id)
        if member:
            roles = [r.id for r in member.roles if r.id != interaction.guild.id]

    await interaction.response.defer(ephemeral=True)
    success, msg = await link_user_on_server(player, target_user.id, roles)

    if success:
        embed = discord.Embed(
            title="✅ Привязка выполнена",
            description=f"Игрок **{player}** успешно привязан к Discord аккаунту {target_user.mention}.",
            color=discord.Color.green()
        )
        embed.add_field(name="Discord ID", value=str(target_user.id), inline=True)
        embed.add_field(name="Передано ролей", value=str(len(roles)), inline=True)
        await interaction.followup.send(embed=embed, ephemeral=True)
    else:
        embed = discord.Embed(
            title="❌ Ошибка привязки",
            description=msg,
            color=discord.Color.red()
        )
        await interaction.followup.send(embed=embed, ephemeral=True)


@bot.tree.command(name="status", description="Проверить статус сервера Space Station 14")
async def slash_status(interaction: discord.Interaction):
    await interaction.response.defer()
    status_url = f"{SS14_API_URL}/status"

    try:
        async with aiohttp.ClientSession() as session:
            try:
                async with session.get(status_url, timeout=aiohttp.ClientTimeout(total=4)) as resp:
                    if resp.status == 200:
                        data = await resp.json()
                        server_title = data.get("name") or SERVER_NAME
                        embed = discord.Embed(
                            title=f"🛸 Статус сервера {server_title}",
                            color=discord.Color.blue()
                        )
                        players = data.get("players", 0)
                        soft_max = data.get("soft_max_players")
                        players_str = f"{players} / {soft_max}" if soft_max else f"{players}"
                        embed.add_field(name="Онлайн", value=f"{players_str} игроков", inline=True)
                        embed.add_field(name="Раунд", value=f"#{data.get('round_id', '?')}", inline=True)
                        embed.add_field(name="Режим", value=str(data.get("preset", "Случайный")), inline=True)
                        if data.get("map"):
                            embed.add_field(name="Карта", value=str(data["map"]), inline=True)
                        await interaction.followup.send(embed=embed)
                        return
            except Exception as e:
                logger.debug(f"/status request failed, trying /admin/info: {e}")

            # Fallback to /admin/info
            info_url = f"{SS14_API_URL}/admin/info"
            headers = {
                "Authorization": f"SS14Token {SS14_API_TOKEN}",
                "Actor": json.dumps({"Guid": "00000000-0000-0000-0000-000000000000", "Name": "DiscordBot"})
            }
            async with session.get(info_url, headers=headers, timeout=aiohttp.ClientTimeout(total=5)) as resp:
                if resp.status == 200:
                    data = await resp.json()
                    embed = discord.Embed(
                        title=f"🛸 Статус сервера {SERVER_NAME}",
                        color=discord.Color.blue()
                    )
                    players_raw = data.get("Players") or data.get("players") or []
                    player_count = len(players_raw) if isinstance(players_raw, list) else players_raw
                    embed.add_field(name="Онлайн", value=f"{player_count} игроков", inline=True)
                    round_id = data.get("RoundId") or data.get("round_id") or "?"
                    embed.add_field(name="Раунд", value=f"#{round_id}", inline=True)
                    preset = data.get("GamePreset") or data.get("preset") or "Случайный"
                    embed.add_field(name="Режим", value=str(preset), inline=True)
                    map_info = data.get("Map") or {}
                    if isinstance(map_info, dict) and map_info.get("Name"):
                        embed.add_field(name="Карта", value=str(map_info["Name"]), inline=True)
                    elif data.get("map"):
                        embed.add_field(name="Карта", value=str(data["map"]), inline=True)
                    await interaction.followup.send(embed=embed)
                else:
                    await interaction.followup.send(f"⚠️ Сервер ответил с кодом {resp.status}")
    except Exception as e:
        await interaction.followup.send(f"❌ Сервер недоступен или выключен ({e}).")


@bot.tree.command(name="whois", description="Проверить информацию о привязанном аккаунте")
@app_commands.describe(user="Пользователь Discord")
async def slash_whois(interaction: discord.Interaction, user: discord.User):
    embed = discord.Embed(
        title=f"Информация о пользователе {user.name}",
        color=discord.Color.purple()
    )
    embed.set_thumbnail(url=user.display_avatar.url)
    embed.add_field(name="Discord ID", value=str(user.id), inline=False)
    embed.add_field(name="Упоминание", value=user.mention, inline=True)
    if interaction.guild:
        member = interaction.guild.get_member(user.id)
        if member:
            roles = [r.mention for r in member.roles if r.id != interaction.guild.id]
            roles_str = " ".join(roles) if roles else "Нет ролей"
            embed.add_field(name="Роли на сервере", value=roles_str[:1024], inline=False)
    await interaction.response.send_message(embed=embed, ephemeral=True)


# ==============================================================================
# OAUTH2 WEB SERVER (AIOHTTP)
# ==============================================================================

async def handle_oauth_callback(request: web.Request) -> web.Response:
    code = request.query.get("code")
    state = request.query.get("state")
    error = request.query.get("error")
    error_description = request.query.get("error_description", "")

    if error:
        logger.warning(f"OAuth error from Discord: {error} - {error_description}")
        return web.Response(
            text=render_html(
                title="Ошибка авторизации",
                heading="Авторизация отменена",
                message=f"Discord вернул ошибку: {error}. Вы отменили авторизацию или произошёл сбой.",
                is_success=False
            ),
            content_type="text/html",
            status=400
        )

    if not code or not state:
        return web.Response(
            text=render_html(
                title="Неверный запрос",
                heading="Отсутствуют параметры",
                message="В запросе отсутствуют обязательные параметры 'code' или 'state'.",
                is_success=False
            ),
            content_type="text/html",
            status=400
        )

    # 1. Verify HMAC state signature
    user_id = verify_hmac_state(state, SS14_SECRET)
    if not user_id:
        logger.warning(f"Invalid HMAC state received: '{state}'")
        return web.Response(
            text=render_html(
                title="Ошибка безопасности",
                heading="Недействительная сессия",
                message="Подпись параметра state недействительна. Запрос мог быть подделан или устарел. Попробуйте нажать кнопку привязки в игре заново.",
                is_success=False
            ),
            content_type="text/html",
            status=403
        )

    # 2. Exchange code for access token
    token_url = "https://discord.com/api/oauth2/token"
    token_data = {
        "client_id": CLIENT_ID,
        "client_secret": CLIENT_SECRET,
        "grant_type": "authorization_code",
        "code": code,
        "redirect_uri": REDIRECT_URI
    }

    try:
        async with aiohttp.ClientSession() as session:
            async with session.post(token_url, data=token_data) as resp:
                if resp.status != 200:
                    resp_text = await resp.text()
                    logger.error(f"Failed to exchange code: {resp.status} - {resp_text}")
                    return web.Response(
                        text=render_html(
                            title="Ошибка обмена кода",
                            heading="Ошибка авторизации Discord",
                            message="Не удалось обменять код авторизации на токен доступа. Попробуйте снова.",
                            is_success=False,
                            extra_info=f"Discord Status: {resp.status}"
                        ),
                        content_type="text/html",
                        status=400
                    )
                token_json = await resp.json()
                access_token = token_json.get("access_token")

            # 3. Get User Profile from Discord
            user_url = "https://discord.com/api/users/@me"
            user_headers = {"Authorization": f"Bearer {access_token}"}
            async with session.get(user_url, headers=user_headers) as resp:
                if resp.status != 200:
                    logger.error(f"Failed to get user profile: {resp.status}")
                    return web.Response(
                        text=render_html(
                            title="Ошибка профиля",
                            heading="Не удалось получить данные профиля",
                            message="Discord не вернул информацию о пользователе.",
                            is_success=False
                        ),
                        content_type="text/html",
                        status=400
                    )
                discord_user = await resp.json()
                discord_id = int(discord_user["id"])
                username = discord_user.get("username", "Неизвестно")

            # 4. Fetch Guild Member Roles if guild_id configured
            roles: List[int] = []
            if GUILD_ID != 0:
                guild_member_url = f"https://discord.com/api/users/@me/guilds/{GUILD_ID}/member"
                async with session.get(guild_member_url, headers=user_headers) as resp:
                    if resp.status == 200:
                        member_data = await resp.json()
                        roles = [int(r) for r in member_data.get("roles", [])]
                        logger.info(f"Retrieved {len(roles)} roles for {username} via OAuth.")
                    else:
                        # Fallback: check bot guild cache
                        guild = bot.get_guild(GUILD_ID)
                        if guild:
                            member = guild.get_member(discord_id)
                            if member:
                                roles = [r.id for r in member.roles if r.id != guild.id]
                                logger.info(f"Retrieved {len(roles)} roles for {username} via bot cache.")

    except Exception as e:
        logger.error(f"Exception during OAuth flow: {e}", exc_info=True)
        return web.Response(
            text=render_html(
                title="Внутренняя ошибка",
                heading="Сбой при обработке запроса",
                message=f"Произошла ошибка при обращении к Discord: {e}",
                is_success=False
            ),
            content_type="text/html",
            status=500
        )

    # 5. Notify SS14 Game Server
    success, msg = await link_user_on_server(user_id, discord_id, roles)
    if not success:
        logger.error(f"Failed to link {user_id} with Discord {discord_id}: {msg}")
        return web.Response(
            text=render_html(
                title="Ошибка привязки",
                heading="Сервер SS14 отклонил привязку",
                message=f"Не удалось связать аккаунты на игровом сервере: {msg}",
                is_success=False,
                extra_info=f"User: {user_id} &bull; Discord ID: {discord_id}"
            ),
            content_type="text/html",
            status=500
        )

    logger.info(f"Successfully linked user {user_id} to Discord {username} ({discord_id}) with {len(roles)} roles.")
    return web.Response(
        text=render_html(
            title="Привязка успешна!",
            heading="Аккаунт успешно привязан!",
            message=f"Ваш аккаунт Discord <b>@{username}</b> успешно связан с вашей учётной записью SS14.<br>Вы можете закрыть эту вкладку и продолжить игру.",
            is_success=True,
            extra_info=f"Игрок: {user_id}<br>Discord: @{username} ({discord_id})<br>Синхронизировано ролей: {len(roles)}"
        ),
        content_type="text/html",
        status=200
    )


async def start_web_app() -> web.AppRunner:
    app = web.Application()
    # Support both /api/auth/discord and /callback
    app.router.add_get("/api/auth/discord", handle_oauth_callback)
    app.router.add_get("/callback", handle_oauth_callback)
    app.router.add_get("/", lambda _: web.Response(text=f"{SERVER_NAME} Discord Auth Service is running."))

    runner = web.AppRunner(app)
    await runner.setup()
    site = web.TCPSite(runner, WEB_HOST, WEB_PORT)
    await site.start()
    logger.info(f"OAuth Web Server started at http://{WEB_HOST}:{WEB_PORT}")
    return runner


# ==============================================================================
# MAIN ENTRYPOINT
# ==============================================================================

async def main():
    if not BOT_TOKEN or BOT_TOKEN == "YOUR_DISCORD_BOT_TOKEN_HERE":
        logger.error("Please set a valid 'bot_token' in config.json before starting!")
        sys.exit(1)

    runner = await start_web_app()

    try:
        await bot.start(BOT_TOKEN)
    except KeyboardInterrupt:
        logger.info("Shutting down...")
    finally:
        await runner.cleanup()
        if not bot.is_closed():
            await bot.close()


if __name__ == "__main__":
    try:
        asyncio.run(main())
    except KeyboardInterrupt:
        pass
