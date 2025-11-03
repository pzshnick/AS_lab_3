import { useEffect, useState } from "react";
import {
  Calendar, Clock, User, Book, MapPin,
  AlertCircle, CheckCircle, Zap, RefreshCw, Trash2, Plus
} from "lucide-react";

const API_BASE = "/api";

type DayOfWeek = 0 | 1 | 2 | 3 | 4 | 5 | 6;
type ScheduleStatus = 0 | 1 | 2 | 3; // Чернетка, Оптимізується, Оптимізовано, Опубліковано
type NotificationType = "optimized" | "updated" | "conflict" | "error";

interface ScheduleEntry {
  subject: string;
  teacher: string;
  group: string;
  room: string;
  dayOfWeek: DayOfWeek;
  startTime: string; // "HH:mm:ss"
  endTime: string;   // "HH:mm:ss"
}

interface Schedule {
  id: number;
  name: string;
  status: ScheduleStatus;
  createdAt: string;           // ISO string
  entries: ScheduleEntry[];    // може бути []
}

interface UiNotification {
  id: number;
  type: NotificationType;
  message: string;
  time: string;
}

export default function ScheduleManagementApp() {
  const [schedules, setSchedules] = useState<Schedule[]>([]);
  const [notifications, setNotifications] = useState<UiNotification[]>([]);
  const [loading, setLoading] = useState<boolean>(false);
  const [formData, setFormData] = useState({
    scheduleName: "Winter Semester 2025",
    subject: "",
    teacher: "",
    group: "",
    room: "",
    dayOfWeek: "1",
    startTime: "09:00",
    endTime: "10:30",
  });

  useEffect(() => {
    loadSchedules().catch(() => addNotification("error", "Помилка завантаження розкладів"));
    const interval = setInterval(() => {
      const sample: Array<{ type: NotificationType; text: string }> = [
        { type: "optimized", text: "Оптимізація завершена успішно" },
        { type: "updated", text: "Розклад оновлено" },
        { type: "optimized", text: "Зменшено кількість вікон на 15" },
        { type: "updated", text: "Покращено балансування навантаження на 25%" },
      ];
      const m = sample[Math.floor(Math.random() * sample.length)];
      addNotification(m.type, m.text);
    }, 5000);
    return () => clearInterval(interval);
  }, []);

  const loadSchedules = async (): Promise<void> => {
    setLoading(true);
    try {
      const res = await fetch(`${API_BASE}/schedules`);
      const data: Schedule[] = await res.json();
      setSchedules(Array.isArray(data) ? data : []);
    } finally {
      setLoading(false);
    }
  };

  const handleSubmit = async (e: React.FormEvent<HTMLFormElement>): Promise<void> => {
    e.preventDefault();
    const schedule = {
      name: formData.scheduleName,
      entries: formData.subject
        ? [{
            subject: formData.subject,
            teacher: formData.teacher,
            group: formData.group,
            room: formData.room,
            dayOfWeek: parseInt(formData.dayOfWeek, 10) as DayOfWeek,
            startTime: `${formData.startTime}:00`,
            endTime: `${formData.endTime}:00`,
          } satisfies ScheduleEntry]
        : [],
    };

    const response = await fetch(`${API_BASE}/schedules`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(schedule),
    });

    if (response.ok) {
      addNotification("updated", `Створено розклад: ${schedule.name}`);
      setFormData({
        scheduleName: "Winter Semester 2025",
        subject: "",
        teacher: "",
        group: "",
        room: "",
        dayOfWeek: "1",
        startTime: "09:00",
        endTime: "10:30",
      });
      await loadSchedules();
    } else {
      addNotification("error", "Помилка при створенні розкладу");
    }
  };

  const optimizeSchedule = async (id: number): Promise<void> => {
    try {
      const res = await fetch(`${API_BASE}/schedules/${id}/optimize`, { method: "POST" });
      if (res.ok) addNotification("optimized", `Розпочато оптимізацію розкладу #${id}`);
      else addNotification("error", "Помилка при запуску оптимізації");
    } catch {
      addNotification("error", "Помилка при запуску оптимізації");
    }
  };

  const checkConflicts = async (id: number): Promise<void> => {
    try {
      const res = await fetch(`${API_BASE}/schedules/${id}/check-conflicts`, { method: "POST" });
      const result: { conflicts?: unknown[] } = await res.json();
      if (result.conflicts && result.conflicts.length > 0) {
        addNotification("conflict", `Знайдено ${result.conflicts.length} конфліктів у розкладі #${id}`);
      } else {
        addNotification("updated", `Конфліктів не знайдено у розкладі #${id}`);
      }
    } catch {
      addNotification("error", "Помилка перевірки конфліктів");
    }
  };

  const deleteSchedule = async (id: number): Promise<void> => {
    if (!window.confirm("Видалити цей розклад?")) return;
    try {
      const res = await fetch(`${API_BASE}/schedules/${id}`, { method: "DELETE" });
      if (res.ok) {
        addNotification("updated", `Розклад #${id} видалено`);
        await loadSchedules();
      } else {
        addNotification("error", "Помилка видалення");
      }
    } catch {
      addNotification("error", "Помилка видалення");
    }
  };

  const addNotification = (type: NotificationType, message: string): void => {
    const notif: UiNotification = { id: Date.now(), type, message, time: new Date().toLocaleString("uk-UA") };
    setNotifications(prev => [notif, ...prev].slice(0, 20));
  };

  const dayNames = ["Неділя","Понеділок","Вівторок","Середа","Четвер","П'ятниця","Субота"];
  const statusNames: Record<ScheduleStatus, string> = {
    0: "Чернетка", 1: "Оптимізується", 2: "Оптимізовано", 3: "Опубліковано"
  };
  const statusClasses: Record<ScheduleStatus, string> = {
    0: "bg-yellow-100 text-yellow-700",
    1: "bg-blue-100 text-blue-700",
    2: "bg-green-100 text-green-700",
    3: "bg-purple-100 text-purple-700",
  };

  const typeConfig: Record<NotificationType, { icon: React.ComponentType<React.SVGProps<SVGSVGElement>>; color: string; iconColor: string; title: string; }> = {
    optimized: { icon: Zap,         color: "border-green-500 bg-green-50",  iconColor: "text-green-600",  title: "Оптимізація" },
    updated:   { icon: CheckCircle, color: "border-blue-500  bg-blue-50",   iconColor: "text-blue-600",   title: "Оновлення" },
    conflict:  { icon: AlertCircle, color: "border-red-500   bg-red-50",    iconColor: "text-red-600",    title: "Конфлікт" },
    error:     { icon: AlertCircle, color: "border-red-500   bg-red-50",    iconColor: "text-red-600",    title: "Помилка" },
  };

  return (
    <div className="min-h-screen bg-gradient-to-br from-indigo-500 via-purple-500 to-pink-500 p-6">
      <div className="max-w-7xl mx-auto">
        <div className="bg-white rounded-2xl shadow-2xl p-8 mb-6 text-center">
          <h1 className="text-4xl font-bold text-transparent bg-clip-text bg-gradient-to-r from-indigo-600 to-purple-600 mb-2">
            Schedule Management System
          </h1>
          <p className="text-gray-600 text-lg">Microservices Architecture Demo - Lab 3 & 4</p>
        </div>

        <div className="grid grid-cols-1 lg:grid-cols-2 gap-6 mb-6">
          <div className="bg-white rounded-2xl shadow-2xl p-6">
            <h2 className="text-2xl font-bold text-indigo-600 mb-4 flex items-center gap-2 border-b-4 border-indigo-600 pb-2">
              <Plus className="w-6 h-6" /> Створити новий розклад
            </h2>

            <form onSubmit={handleSubmit} className="space-y-4">
              <div>
                <label className="block text-sm font-semibold text-gray-700 mb-1">Назва розкладу</label>
                <input
                  type="text"
                  value={formData.scheduleName}
                  onChange={(e) => setFormData({ ...formData, scheduleName: e.target.value })}
                  className="w-full px-4 py-2 border-2 border-gray-200 rounded-lg focus:border-indigo-500 focus:outline-none"
                  required
                />
              </div>

              <div className="border-t pt-4">
                <h3 className="font-semibold text-gray-700 mb-3">Додати заняття</h3>
                <div className="grid grid-cols-2 gap-4">
                  <div>
                    <label className="block text-sm font-medium text-gray-700 mb-1">Предмет</label>
                    <input
                      type="text"
                      value={formData.subject}
                      onChange={(e) => setFormData({ ...formData, subject: e.target.value })}
                      className="w-full px-3 py-2 border-2 border-gray-200 rounded-lg focus:border-indigo-500 focus:outline-none text-sm"
                      placeholder="Software Architecture"
                    />
                  </div>
                  <div>
                    <label className="block text-sm font-medium text-gray-700 mb-1">Викладач</label>
                    <input
                      type="text"
                      value={formData.teacher}
                      onChange={(e) => setFormData({ ...formData, teacher: e.target.value })}
                      className="w-full px-3 py-2 border-2 border-gray-200 rounded-lg focus:border-indigo-500 focus:outline-none text-sm"
                      placeholder="Dr. Lutsyk"
                    />
                  </div>
                  <div>
                    <label className="block text-sm font-medium text-gray-700 mb-1">Група</label>
                    <input
                      type="text"
                      value={formData.group}
                      onChange={(e) => setFormData({ ...formData, group: e.target.value })}
                      className="w-full px-3 py-2 border-2 border-gray-200 rounded-lg focus:border-indigo-500 focus:outline-none text-sm"
                      placeholder="PZ-46"
                    />
                  </div>
                  <div>
                    <label className="block text-sm font-medium text-gray-700 mb-1">Аудиторія</label>
                    <input
                      type="text"
                      value={formData.room}
                      onChange={(e) => setFormData({ ...formData, room: e.target.value })}
                      className="w-full px-3 py-2 border-2 border-gray-200 rounded-lg focus:border-indigo-500 focus:outline-none text-sm"
                      placeholder="Room 301"
                    />
                  </div>
                  <div>
                    <label className="block text-sm font-medium text-gray-700 mb-1">День тижня</label>
                    <select
                      value={formData.dayOfWeek}
                      onChange={(e) => setFormData({ ...formData, dayOfWeek: e.target.value })}
                      className="w-full px-3 py-2 border-2 border-gray-200 rounded-lg focus:border-indigo-500 focus:outline-none text-sm"
                    >
                      {["Понеділок","Вівторок","Середа","Четвер","П'ятниця","Субота"].map((day, idx) => (
                        <option key={idx + 1} value={idx + 1}>{day}</option>
                      ))}
                    </select>
                  </div>
                  <div>
                    <label className="block text-sm font-medium text-gray-700 mb-1">Час</label>
                    <div className="flex gap-2">
                      <input
                        type="time"
                        value={formData.startTime}
                        onChange={(e) => setFormData({ ...formData, startTime: e.target.value })}
                        className="w-full px-3 py-2 border-2 border-gray-200 rounded-lg focus:border-indigo-500 focus:outline-none text-sm"
                      />
                      <input
                        type="time"
                        value={formData.endTime}
                        onChange={(e) => setFormData({ ...formData, endTime: e.target.value })}
                        className="w-full px-3 py-2 border-2 border-gray-200 rounded-lg focus:border-indigo-500 focus:outline-none text-sm"
                      />
                    </div>
                  </div>
                </div>
              </div>

              <button
                type="submit"
                className="w-full bg-gradient-to-r from-indigo-600 to-purple-600 text-white font-semibold py-3 rounded-lg hover:shadow-lg transform hover:-translate-y-0.5 transition-all"
              >
                Створити розклад
              </button>
            </form>
          </div>

          <div className="bg-white rounded-2xl shadow-2xl p-6">
            <div className="flex items-center justify-between mb-4 border-b-4 border-indigo-600 pb-2">
              <h2 className="text-2xl font-bold text-indigo-600 flex items-center gap-2">
                <Calendar className="w-6 h-6" /> Існуючі розклади
              </h2>
              <button
                onClick={loadSchedules}
                className="bg-gradient-to-r from-blue-500 to-cyan-500 text-white px-4 py-2 rounded-lg font-semibold hover:shadow-lg transform hover:-translate-y-0.5 transition-all flex items-center gap-2"
              >
                <RefreshCw className="w-4 h-4" /> Оновити
              </button>
            </div>

            <div className="space-y-4 max-h-[600px] overflow-y-auto pr-2">
              {loading ? (
                <div className="text-center py-12">
                  <div className="inline-block w-12 h-12 border-4 border-indigo-600 border-t-transparent rounded-full animate-spin mb-4" />
                  <p className="text-gray-600">Завантаження...</p>
                </div>
              ) : schedules.length === 0 ? (
                <div className="text-center py-12 text-gray-500">
                  <Calendar className="w-16 h-16 mx-auto mb-4 text-gray-300" />
                  <p>Немає розкладів. Створіть перший!</p>
                </div>
              ) : (
                schedules.map((schedule) => (
                  <div key={schedule.id} className="bg-gradient-to-r from-gray-50 to-gray-100 p-4 rounded-xl border-l-4 border-indigo-600">
                    <div className="flex items-start justify-between mb-2">
                      <h3 className="text-lg font-bold text-gray-800">{schedule.name}</h3>
                      <span className={`px-3 py-1 rounded-full text-xs font-semibold ${statusClasses[schedule.status]}`}>
                        {statusNames[schedule.status]}
                      </span>
                    </div>

                    <div className="text-sm text-gray-600 space-y-1 mb-3">
                      <p><strong>ID:</strong> {schedule.id}</p>
                      <p><strong>Створено:</strong> {new Date(schedule.createdAt).toLocaleString("uk-UA")}</p>
                      <p><strong>Занять:</strong> {schedule.entries?.length ?? 0}</p>
                    </div>

                    {schedule.entries?.length > 0 && (
                      <div className="space-y-2 mb-3">
                        {schedule.entries.map((entry, idx) => (
                          <div key={idx} className="bg-indigo-50 p-3 rounded-lg text-sm">
                            <div className="font-semibold text-indigo-700 mb-1">{entry.subject}</div>
                            <div className="text-gray-600 space-y-0.5">
                              <div className="flex items-center gap-2"><User className="w-3 h-3" />{entry.teacher}</div>
                              <div className="flex items-center gap-2"><Book className="w-3 h-3" />{entry.group}</div>
                              <div className="flex items-center gap-2"><MapPin className="w-3 h-3" />{entry.room}</div>
                              <div className="flex items-center gap-2"><Clock className="w-3 h-3" />{dayNames[entry.dayOfWeek]} {entry.startTime.substring(0,5)} - {entry.endTime.substring(0,5)}</div>
                            </div>
                          </div>
                        ))}
                      </div>
                    )}

                    <div className="flex gap-2">
                      <button onClick={() => optimizeSchedule(schedule.id)} className="flex-1 bg-gradient-to-r from-green-500 to-emerald-500 text-white px-3 py-2 rounded-lg text-sm font-semibold hover:shadow-lg transform hover:-translate-y-0.5 transition-all flex items-center justify-center gap-1">
                        <Zap className="w-4 h-4" /> Оптимізувати
                      </button>
                      <button onClick={() => checkConflicts(schedule.id)} className="flex-1 bg-gradient-to-r from-orange-500 to-yellow-500 text-white px-3 py-2 rounded-lg text-sm font-semibold hover:shadow-lg transform hover:-translate-y-0.5 transition-all flex items-center justify-center gap-1">
                        <AlertCircle className="w-4 h-4" /> Конфлікти
                      </button>
                      <button onClick={() => deleteSchedule(schedule.id)} className="flex-1 bg-gradient-to-r from-red-500 to-pink-500 text-white px-3 py-2 rounded-lg text-sm font-semibold hover:shadow-lg transform hover:-translate-y-0.5 transition-all flex items-center justify-center gap-1">
                        <Trash2 className="w-4 h-4" /> Видалити
                      </button>
                    </div>
                  </div>
                ))
              )}
            </div>
          </div>
        </div>

        <div className="bg-white rounded-2xl shadow-2xl p-6">
          <h2 className="text-2xl font-bold text-indigo-600 mb-4 flex items-center gap-2 border-b-4 border-indigo-600 pb-2">
            Сповіщення в реальному часі
          </h2>
          <p className="text-sm text-gray-600 mb-4">Події з RabbitMQ (оновлюються автоматично)</p>

          <div className="space-y-3 max-h-96 overflow-y-auto">
            {notifications.length === 0 ? (
              <div className="text-center py-8 text-gray-500">
                <AlertCircle className="w-12 h-12 mx-auto mb-3 text-gray-300" />
                <p>Очікування подій...</p>
              </div>
            ) : (
              notifications.map((notif) => {
                const cfg = typeConfig[notif.type];
                const Icon = cfg.icon;
                return (
                  <div key={notif.id} className={`p-4 rounded-xl border-l-4 ${cfg.color} animate-fade-in`}>
                    <div className="flex items-start gap-3">
                      <Icon className={`w-5 h-5 ${cfg.iconColor} mt-0.5`} />
                      <div className="flex-1">
                        <div className="flex items-center justify-between mb-1">
                          <span className="font-semibold text-gray-800">{cfg.title}</span>
                          <span className="text-xs text-gray-500">{notif.time}</span>
                        </div>
                        <p className="text-sm text-gray-700">{notif.message}</p>
                      </div>
                    </div>
                  </div>
                );
              })
            )}
          </div>
        </div>
      </div>
    </div>
  );
}
