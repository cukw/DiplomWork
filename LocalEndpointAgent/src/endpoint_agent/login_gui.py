from __future__ import annotations

from getpass import getpass
from pathlib import Path
from threading import Thread

from .config import AgentConfig
from .enrollment import enroll_computer
from .prod_defaults import DEFAULT_AGENT_AUTH_HEADER, DEFAULT_AGENT_AUTH_TOKEN


def prompt_login_and_enroll(config_path: str | Path, cfg: AgentConfig) -> bool:
    try:
        import tkinter as tk
        from tkinter import messagebox
    except Exception:
        return _console_login_and_enroll(config_path, cfg)

    result = {"ok": False}
    login_in_progress = {"value": False}

    root = tk.Tk()
    root.title("Local Endpoint Agent")
    root.resizable(False, False)

    width = 380
    height = 240
    x = max(0, (root.winfo_screenwidth() - width) // 2)
    y = max(0, (root.winfo_screenheight() - height) // 2)
    root.geometry(f"{width}x{height}+{x}+{y}")

    frame = tk.Frame(root, padx=24, pady=20)
    frame.pack(fill="both", expand=True)

    tk.Label(frame, text="Авторизация локального агента", font=("Segoe UI", 12, "bold")).pack(anchor="w")
    tk.Label(frame, text="Введите учетные данные системы мониторинга.", font=("Segoe UI", 9)).pack(anchor="w", pady=(4, 14))

    tk.Label(frame, text="Логин", font=("Segoe UI", 9)).pack(anchor="w")
    username_var = tk.StringVar()
    username_entry = tk.Entry(frame, textvariable=username_var, font=("Segoe UI", 10))
    username_entry.pack(fill="x", pady=(2, 10))

    tk.Label(frame, text="Пароль", font=("Segoe UI", 9)).pack(anchor="w")
    password_var = tk.StringVar()
    password_entry = tk.Entry(frame, textvariable=password_var, show="*", font=("Segoe UI", 10))
    password_entry.pack(fill="x", pady=(2, 12))

    status_var = tk.StringVar(value="")
    tk.Label(frame, textvariable=status_var, fg="#6b7280", font=("Segoe UI", 9)).pack(anchor="w", pady=(0, 10))

    buttons = tk.Frame(frame)
    buttons.pack(fill="x")

    def set_busy(is_busy: bool) -> None:
        login_button.configure(state="disabled" if is_busy else "normal")
        close_button.configure(state="disabled" if is_busy else "normal")
        username_entry.configure(state="disabled" if is_busy else "normal")
        password_entry.configure(state="disabled" if is_busy else "normal")

    def on_login() -> None:
        if login_in_progress["value"]:
            return

        username = username_var.get().strip()
        password = password_var.get()
        if not username or not password:
            messagebox.showerror("Ошибка", "Введите логин и пароль.")
            return

        login_in_progress["value"] = True
        set_busy(True)
        status_var.set("Вход и регистрация компьютера...")

        def worker() -> None:
            try:
                enrollment = _enroll(config_path, cfg, username, password)
            except Exception as exc:
                root.after(0, on_error, exc)
                return
            root.after(0, on_success, enrollment)

        Thread(target=worker, daemon=True).start()

    def on_success(enrollment: dict[str, object]) -> None:
        result["ok"] = True
        status_var.set("Готово.")
        messagebox.showinfo("Локальный агент", _success_message(enrollment))
        root.destroy()

    def on_error(exc: Exception) -> None:
        login_in_progress["value"] = False
        set_busy(False)
        status_var.set("")
        messagebox.showerror("Ошибка авторизации агента", str(exc))

    login_button = tk.Button(buttons, text="Войти", command=on_login, width=14)
    login_button.pack(side="right")
    close_button = tk.Button(buttons, text="Закрыть", command=root.destroy, width=14)
    close_button.pack(side="right", padx=(0, 8))

    username_entry.focus_set()
    root.bind("<Return>", lambda _event: on_login())
    root.mainloop()
    return bool(result["ok"])


def _console_login_and_enroll(config_path: str | Path, cfg: AgentConfig) -> bool:
    username = input("Username: ").strip()
    password = getpass("Password: ")
    if not username or not password:
        return False
    enrollment = _enroll(config_path, cfg, username, password)
    print(_success_message(enrollment))
    return True


def _enroll(config_path: str | Path, cfg: AgentConfig, username: str, password: str) -> dict[str, object]:
    return enroll_computer(
        gateway_url=cfg.services.gateway_url,
        username=username,
        password=password,
        config_path=config_path,
        insecure_tls=cfg.services.gateway_tls_insecure,
        activity_service_url=cfg.services.activity_service_url,
        agent_management_url=cfg.services.agent_management_url,
        agent_auth_token=cfg.security.agent_transport_auth.token or DEFAULT_AGENT_AUTH_TOKEN,
        agent_auth_header=cfg.security.agent_transport_auth.header_name or DEFAULT_AGENT_AUTH_HEADER,
    )


def _success_message(enrollment: dict[str, object]) -> str:
    computer = enrollment.get("computer") or {}
    computer_id = "неизвестен"
    if isinstance(computer, dict):
        computer_id = str(computer.get("id") or computer_id)

    session_id = str(enrollment.get("sessionId") or "неизвестна")
    session_expires_at = str(enrollment.get("sessionExpiresAt") or "не указано")
    return (
        "Агент запущен.\n"
        f"Компьютер ID: {computer_id}\n"
        f"Сессия ID: {session_id}\n"
        f"Сессия до: {session_expires_at}"
    )
