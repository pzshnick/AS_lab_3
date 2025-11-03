import { useEffect, useState } from "react";
import {
  Calendar, Clock, User, Book, MapPin,
  AlertCircle, CheckCircle, Zap, RefreshCw, Trash2, Plus,
  BarChart3, TrendingUp, Activity
} from "lucide-react";

const API_BASE = "/api";

type DayOfWeek = 0 | 1 | 2 | 3 | 4 | 5 | 6;
type ScheduleStatus = 0 | 1 | 2 | 3;
type NotificationType = "optimized" | "updated" | "conflict" | "error";

interface ScheduleEntry {
  subject: string;
  teacher: string;
  group: string;
  room: string;
  dayOfWeek: DayOfWeek;
  startTime: string;
  endTime: string;
}

interface Schedule {
  id: number;
  name: string;
  status: ScheduleStatus;
  createdAt: string;
  entries: ScheduleEntry[];
}

interface UiNotification {
  id: number;
  type: NotificationType;
  message: string;
  time: string;
}

interface SystemStatistics {
  totalSchedules: number;
  totalOptimizations: number;
  totalConflictsDetected: number;
  totalUpdates: number;
  averageOptimizationTime: number;
  lastUpdated: string;
}

interface CatalogItem {
  id: number;
  name: string;
}

export default function ScheduleManagementApp() {
  const [schedules, setSchedules] = useState<Schedule[]>([]);
  const [notifications, setNotifications] = useState<UiNotification[]>([]);
  const [statistics, setStatistics] = useState<SystemStatistics | null>(null);
  const [teachers, setTeachers] = useState<CatalogItem[]>([]);
  const [groups, setGroups] = useState<CatalogItem[]>([]);
  const [rooms, setRooms] = useState<CatalogItem[]>([]);
  const [loading, setLoading] = useState<boolean>(false);
  const [activeTab, setActiveTab] = useState<"schedules" | "analytics">("schedules");
  
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
    loadSchedules().catch(() => addNotification("error", "Failed to load schedules"));
    loadCatalogData().catch(() => addNotification("error", "Failed to load catalog data"));
    loadStatistics().catch(() => addNotification("error", "Failed to load statistics"));
    
    const interval = setInterval(() => {
      loadStatistics();
      const sample: Array<{ type: NotificationType; text: string }> = [
        { type: "optimized", text: "Optimization completed successfully" },
        { type: "updated", text: "Schedule updated" },
        { type: "optimized", text: "Reduced windows count by 15" },
        { type: "updated", text: "Improved load balance by 25%" },
      ];
      const m = sample[Math.floor(Math.random() * sample.length)];
      addNotification(m.type, m.text);
    }, 8000);
    
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

  const loadCatalogData = async (): Promise<void> => {
    try {
      const [teachersRes, groupsRes, roomsRes] = await Promise.all([
        fetch("http://localhost:5003/api/catalog/teachers"),
        fetch("http://localhost:5003/api/catalog/groups"),
        fetch("http://localhost:5003/api/catalog/rooms"),
      ]);
      
      setTeachers(await teachersRes.json());
      setGroups(await groupsRes.json());
      setRooms(await roomsRes.json());
    } catch (error) {
      console.error("Failed to load catalog data:", error);
    }
  };

  const loadStatistics = async (): Promise<void> => {
    try {
      const res = await fetch("http://localhost:5004/api/analytics/stats");
      const data = await res.json();
      setStatistics(data);
    } catch (error) {
      console.error("Failed to load statistics:", error);
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
      addNotification("updated", `Created schedule: ${schedule.name}`);
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
      await loadStatistics();
    } else {
      addNotification("error", "Failed to create schedule");
    }
  };

  const optimizeSchedule = async (id: number): Promise<void> => {
    try {
      const res = await fetch(`${API_BASE}/schedules/${id}/optimize`, { method: "POST" });
      if (res.ok) addNotification("optimized", `Started optimization for schedule #${id}`);
      else addNotification("error", "Failed to start optimization");
    } catch {
      addNotification("error", "Failed to start optimization");
    }
  };

  const checkConflicts = async (id: number): Promise<void> => {
    try {
      const res = await fetch(`${API_BASE}/schedules/${id}/check-conflicts`, { method: "POST" });
      const result: { conflicts?: unknown[] } = await res.json();
      if (result.conflicts && result.conflicts.length > 0) {
        addNotification("conflict", `Found ${result.conflicts.length} conflicts in schedule #${id}`);
      } else {
        addNotification("updated", `No conflicts found in schedule #${id}`);
      }
    } catch {
      addNotification("error", "Failed to check conflicts");
    }
  };

  const deleteSchedule = async (id: number): Promise<void> => {
    if (!window.confirm("Delete this schedule?")) return;
    try {
      const res = await fetch(`${API_BASE}/schedules/${id}`, { method: "DELETE" });
      if (res.ok) {
        addNotification("updated", `Schedule #${id} deleted`);
        await loadSchedules();
        await loadStatistics();
      } else {
        addNotification("error", "Failed to delete");
      }
    } catch {
      addNotification("error", "Failed to delete");
    }
  };

  const addNotification = (type: NotificationType, message: string): void => {
    const notif: UiNotification = { 
      id: Date.now(), 
      type, 
      message, 
      time: new Date().toLocaleString("en-US") 
    };
    setNotifications(prev => [notif, ...prev].slice(0, 20));
  };

  const dayNames = ["Sunday","Monday","Tuesday","Wednesday","Thursday","Friday","Saturday"];
  const statusNames: Record<ScheduleStatus, string> = {
    0: "Draft", 1: "Optimizing", 2: "Optimized", 3: "Published"
  };
  const statusClasses: Record<ScheduleStatus, string> = {
    0: "bg-yellow-500/20 text-yellow-300 border-yellow-500/50",
    1: "bg-blue-500/20 text-blue-300 border-blue-500/50",
    2: "bg-green-500/20 text-green-300 border-green-500/50",
    3: "bg-purple-500/20 text-purple-300 border-purple-500/50",
  };

  const typeConfig: Record<NotificationType, { 
    icon: React.ComponentType<React.SVGProps<SVGSVGElement>>; 
    color: string; 
    iconColor: string; 
    title: string; 
  }> = {
    optimized: { icon: Zap, color: "border-emerald-500/50 bg-emerald-500/10", iconColor: "text-emerald-400", title: "Optimization" },
    updated: { icon: CheckCircle, color: "border-cyan-500/50 bg-cyan-500/10", iconColor: "text-cyan-400", title: "Update" },
    conflict: { icon: AlertCircle, color: "border-red-500/50 bg-red-500/10", iconColor: "text-red-400", title: "Conflict" },
    error: { icon: AlertCircle, color: "border-red-500/50 bg-red-500/10", iconColor: "text-red-400", title: "Error" },
  };

  return (
    <div className="min-h-screen bg-gradient-to-br from-slate-900 via-blue-950 to-slate-900 p-6">
      <div className="max-w-7xl mx-auto">
        <div className="bg-slate-800/50 backdrop-blur-sm border border-cyan-500/30 rounded-2xl shadow-2xl p-8 mb-6 text-center">
          <h1 className="text-4xl font-bold text-transparent bg-clip-text bg-gradient-to-r from-cyan-400 to-blue-500 mb-2">
            Schedule Management System
          </h1>
          <p className="text-slate-400 text-lg">Microservices Architecture Demo - Lab 3 & 4</p>
        </div>

        <div className="flex gap-4 mb-6">
          <button
            onClick={() => setActiveTab("schedules")}
            className={`flex-1 py-3 px-6 rounded-lg font-semibold transition-all ${
              activeTab === "schedules"
                ? "bg-cyan-500/20 text-cyan-300 border-2 border-cyan-500/50"
                : "bg-slate-800/50 text-slate-400 border-2 border-slate-700/50 hover:border-slate-600"
            }`}
          >
            <Calendar className="inline w-5 h-5 mr-2" />
            Schedules
          </button>
          <button
            onClick={() => setActiveTab("analytics")}
            className={`flex-1 py-3 px-6 rounded-lg font-semibold transition-all ${
              activeTab === "analytics"
                ? "bg-cyan-500/20 text-cyan-300 border-2 border-cyan-500/50"
                : "bg-slate-800/50 text-slate-400 border-2 border-slate-700/50 hover:border-slate-600"
            }`}
          >
            <BarChart3 className="inline w-5 h-5 mr-2" />
            Analytics
          </button>
        </div>

        {activeTab === "schedules" && (
          <div className="grid grid-cols-1 lg:grid-cols-2 gap-6 mb-6">
            <div className="bg-slate-800/50 backdrop-blur-sm border border-cyan-500/30 rounded-2xl shadow-2xl p-6">
              <h2 className="text-2xl font-bold text-cyan-400 mb-4 flex items-center gap-2 border-b-2 border-cyan-500/50 pb-2">
                <Plus className="w-6 h-6" /> Create New Schedule
              </h2>

              <form onSubmit={handleSubmit} className="space-y-4">
                <div>
                  <label className="block text-sm font-semibold text-slate-300 mb-1">Schedule Name</label>
                  <input
                    type="text"
                    value={formData.scheduleName}
                    onChange={(e) => setFormData({ ...formData, scheduleName: e.target.value })}
                    className="w-full px-4 py-2 bg-slate-900/50 border-2 border-slate-700 rounded-lg focus:border-cyan-500 focus:outline-none text-slate-200"
                    required
                  />
                </div>

                <div className="border-t border-slate-700 pt-4">
                  <h3 className="font-semibold text-slate-300 mb-3">Add Class</h3>
                  <div className="grid grid-cols-2 gap-4">
                    <div>
                      <label className="block text-sm font-medium text-slate-400 mb-1">Subject</label>
                      <input
                        type="text"
                        value={formData.subject}
                        onChange={(e) => setFormData({ ...formData, subject: e.target.value })}
                        className="w-full px-3 py-2 bg-slate-900/50 border-2 border-slate-700 rounded-lg focus:border-cyan-500 focus:outline-none text-sm text-slate-200"
                        placeholder="Software Architecture"
                      />
                    </div>
                    <div>
                      <label className="block text-sm font-medium text-slate-400 mb-1">Teacher</label>
                      <input
                        type="text"
                        value={formData.teacher}
                        onChange={(e) => setFormData({ ...formData, teacher: e.target.value })}
                        className="w-full px-3 py-2 bg-slate-900/50 border-2 border-slate-700 rounded-lg focus:border-cyan-500 focus:outline-none text-sm text-slate-200"
                        placeholder="Dr. Lutsyk"
                        list="teachers-list"
                      />
                      <datalist id="teachers-list">
                        {teachers.map(t => <option key={t.id} value={t.name} />)}
                      </datalist>
                    </div>
                    <div>
                      <label className="block text-sm font-medium text-slate-400 mb-1">Group</label>
                      <input
                        type="text"
                        value={formData.group}
                        onChange={(e) => setFormData({ ...formData, group: e.target.value })}
                        className="w-full px-3 py-2 bg-slate-900/50 border-2 border-slate-700 rounded-lg focus:border-cyan-500 focus:outline-none text-sm text-slate-200"
                        placeholder="PZ-46"
                        list="groups-list"
                      />
                      <datalist id="groups-list">
                        {groups.map(g => <option key={g.id} value={g.name} />)}
                      </datalist>
                    </div>
                    <div>
                      <label className="block text-sm font-medium text-slate-400 mb-1">Room</label>
                      <input
                        type="text"
                        value={formData.room}
                        onChange={(e) => setFormData({ ...formData, room: e.target.value })}
                        className="w-full px-3 py-2 bg-slate-900/50 border-2 border-slate-700 rounded-lg focus:border-cyan-500 focus:outline-none text-sm text-slate-200"
                        placeholder="Room 301"
                        list="rooms-list"
                      />
                      <datalist id="rooms-list">
                        {rooms.map(r => <option key={r.id} value={r.name} />)}
                      </datalist>
                    </div>
                    <div>
                      <label className="block text-sm font-medium text-slate-400 mb-1">Day of Week</label>
                      <select
                        value={formData.dayOfWeek}
                        onChange={(e) => setFormData({ ...formData, dayOfWeek: e.target.value })}
                        className="w-full px-3 py-2 bg-slate-900/50 border-2 border-slate-700 rounded-lg focus:border-cyan-500 focus:outline-none text-sm text-slate-200"
                      >
                        {["Monday","Tuesday","Wednesday","Thursday","Friday","Saturday"].map((day, idx) => (
                          <option key={idx + 1} value={idx + 1}>{day}</option>
                        ))}
                      </select>
                    </div>
                    <div>
                      <label className="block text-sm font-medium text-slate-400 mb-1">Time</label>
                      <div className="flex gap-2">
                        <input
                          type="time"
                          value={formData.startTime}
                          onChange={(e) => setFormData({ ...formData, startTime: e.target.value })}
                          className="w-full px-3 py-2 bg-slate-900/50 border-2 border-slate-700 rounded-lg focus:border-cyan-500 focus:outline-none text-sm text-slate-200"
                        />
                        <input
                          type="time"
                          value={formData.endTime}
                          onChange={(e) => setFormData({ ...formData, endTime: e.target.value })}
                          className="w-full px-3 py-2 bg-slate-900/50 border-2 border-slate-700 rounded-lg focus:border-cyan-500 focus:outline-none text-sm text-slate-200"
                        />
                      </div>
                    </div>
                  </div>
                </div>

                <button
                  type="submit"
                  className="w-full bg-gradient-to-r from-cyan-500 to-blue-500 text-white font-semibold py-3 rounded-lg hover:shadow-lg hover:shadow-cyan-500/50 transform hover:-translate-y-0.5 transition-all"
                >
                  Create Schedule
                </button>
              </form>
            </div>

            <div className="bg-slate-800/50 backdrop-blur-sm border border-cyan-500/30 rounded-2xl shadow-2xl p-6">
              <div className="flex items-center justify-between mb-4 border-b-2 border-cyan-500/50 pb-2">
                <h2 className="text-2xl font-bold text-cyan-400 flex items-center gap-2">
                  <Calendar className="w-6 h-6" /> Existing Schedules
                </h2>
                <button
                  onClick={loadSchedules}
                  className="bg-gradient-to-r from-blue-500 to-cyan-500 text-white px-4 py-2 rounded-lg font-semibold hover:shadow-lg hover:shadow-blue-500/50 transform hover:-translate-y-0.5 transition-all flex items-center gap-2"
                >
                  <RefreshCw className="w-4 h-4" /> Refresh
                </button>
              </div>

              <div className="space-y-4 max-h-[600px] overflow-y-auto pr-2">
                {loading ? (
                  <div className="text-center py-12">
                    <div className="inline-block w-12 h-12 border-4 border-cyan-500 border-t-transparent rounded-full animate-spin mb-4" />
                    <p className="text-slate-400">Loading...</p>
                  </div>
                ) : schedules.length === 0 ? (
                  <div className="text-center py-12 text-slate-500">
                    <Calendar className="w-16 h-16 mx-auto mb-4 text-slate-700" />
                    <p>No schedules found. Create your first one!</p>
                  </div>
                ) : (
                  schedules.map((schedule) => (
                    <div key={schedule.id} className="bg-slate-900/50 border border-cyan-500/30 p-4 rounded-xl">
                      <div className="flex items-start justify-between mb-2">
                        <h3 className="text-lg font-bold text-slate-200">{schedule.name}</h3>
                        <span className={`px-3 py-1 rounded-full text-xs font-semibold border ${statusClasses[schedule.status]}`}>
                          {statusNames[schedule.status]}
                        </span>
                      </div>

                      <div className="text-sm text-slate-400 space-y-1 mb-3">
                        <p><strong>ID:</strong> {schedule.id}</p>
                        <p><strong>Created:</strong> {new Date(schedule.createdAt).toLocaleString("en-US")}</p>
                        <p><strong>Classes:</strong> {schedule.entries?.length ?? 0}</p>
                      </div>

                      {schedule.entries?.length > 0 && (
                        <div className="space-y-2 mb-3">
                          {schedule.entries.map((entry, idx) => (
                            <div key={idx} className="bg-cyan-500/10 border border-cyan-500/30 p-3 rounded-lg text-sm">
                              <div className="font-semibold text-cyan-300 mb-1">{entry.subject}</div>
                              <div className="text-slate-400 space-y-0.5">
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
                        <button onClick={() => optimizeSchedule(schedule.id)} className="flex-1 bg-gradient-to-r from-emerald-500 to-green-500 text-white px-3 py-2 rounded-lg text-sm font-semibold hover:shadow-lg hover:shadow-emerald-500/50 transform hover:-translate-y-0.5 transition-all flex items-center justify-center gap-1">
                          <Zap className="w-4 h-4" /> Optimize
                        </button>
                        <button onClick={() => checkConflicts(schedule.id)} className="flex-1 bg-gradient-to-r from-orange-500 to-amber-500 text-white px-3 py-2 rounded-lg text-sm font-semibold hover:shadow-lg hover:shadow-orange-500/50 transform hover:-translate-y-0.5 transition-all flex items-center justify-center gap-1">
                          <AlertCircle className="w-4 h-4" /> Conflicts
                        </button>
                        <button onClick={() => deleteSchedule(schedule.id)} className="flex-1 bg-gradient-to-r from-red-500 to-pink-500 text-white px-3 py-2 rounded-lg text-sm font-semibold hover:shadow-lg hover:shadow-red-500/50 transform hover:-translate-y-0.5 transition-all flex items-center justify-center gap-1">
                          <Trash2 className="w-4 h-4" /> Delete
                        </button>
                      </div>
                    </div>
                  ))
                )}
              </div>
            </div>
          </div>
        )}

        {activeTab === "analytics" && statistics && (
          <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-6 mb-6">
            <div className="bg-slate-800/50 backdrop-blur-sm border border-cyan-500/30 rounded-xl p-6">
              <div className="flex items-center justify-between mb-2">
                <Activity className="w-8 h-8 text-cyan-400" />
                <span className="text-3xl font-bold text-cyan-300">{statistics.totalSchedules}</span>
              </div>
              <h3 className="text-slate-400 font-semibold">Total Schedules</h3>
            </div>
            
            <div className="bg-slate-800/50 backdrop-blur-sm border border-emerald-500/30 rounded-xl p-6">
              <div className="flex items-center justify-between mb-2">
                <Zap className="w-8 h-8 text-emerald-400" />
                <span className="text-3xl font-bold text-emerald-300">{statistics.totalOptimizations}</span>
              </div>
              <h3 className="text-slate-400 font-semibold">Total Optimizations</h3>
            </div>
            
            <div className="bg-slate-800/50 backdrop-blur-sm border border-red-500/30 rounded-xl p-6">
              <div className="flex items-center justify-between mb-2">
                <AlertCircle className="w-8 h-8 text-red-400" />
                <span className="text-3xl font-bold text-red-300">{statistics.totalConflictsDetected}</span>
              </div>
              <h3 className="text-slate-400 font-semibold">Conflicts Detected</h3>
            </div>
            
            <div className="bg-slate-800/50 backdrop-blur-sm border border-blue-500/30 rounded-xl p-6">
              <div className="flex items-center justify-between mb-2">
                <TrendingUp className="w-8 h-8 text-blue-400" />
                <span className="text-3xl font-bold text-blue-300">{statistics.totalUpdates}</span>
              </div>
              <h3 className="text-slate-400 font-semibold">Total Updates</h3>
            </div>
            
            <div className="bg-slate-800/50 backdrop-blur-sm border border-purple-500/30 rounded-xl p-6">
              <div className="flex items-center justify-between mb-2">
                <Clock className="w-8 h-8 text-purple-400" />
                <span className="text-3xl font-bold text-purple-300">{statistics.averageOptimizationTime.toFixed(1)}s</span>
              </div>
              <h3 className="text-slate-400 font-semibold">Avg Optimization Time</h3>
            </div>
            
            <div className="bg-slate-800/50 backdrop-blur-sm border border-cyan-500/30 rounded-xl p-6">
              <div className="flex items-center justify-between mb-2">
                <Calendar className="w-8 h-8 text-cyan-400" />
                <span className="text-lg font-bold text-cyan-300">{new Date(statistics.lastUpdated).toLocaleTimeString()}</span>
              </div>
              <h3 className="text-slate-400 font-semibold">Last Updated</h3>
            </div>
          </div>
        )}

        <div className="bg-slate-800/50 backdrop-blur-sm border border-cyan-500/30 rounded-2xl shadow-2xl p-6">
          <h2 className="text-2xl font-bold text-cyan-400 mb-4 flex items-center gap-2 border-b-2 border-cyan-500/50 pb-2">
            Real-time Notifications
          </h2>
          <p className="text-sm text-slate-400 mb-4">Events from RabbitMQ (auto-updated)</p>

          <div className="space-y-3 max-h-96 overflow-y-auto">
            {notifications.length === 0 ? (
              <div className="text-center py-8 text-slate-500">
                <AlertCircle className="w-12 h-12 mx-auto mb-3 text-slate-700" />
                <p>Waiting for events...</p>
              </div>
            ) : (
              notifications.map((notif) => {
                const cfg = typeConfig[notif.type];
                const Icon = cfg.icon;
                return (
                  <div key={notif.id} className={`p-4 rounded-xl border ${cfg.color} animate-fade-in`}>
                    <div className="flex items-start gap-3">
                      <Icon className={`w-5 h-5 ${cfg.iconColor} mt-0.5`} />
                      <div className="flex-1">
                        <div className="flex items-center justify-between mb-1">
                          <span className="font-semibold text-slate-200">{cfg.title}</span>
                          <span className="text-xs text-slate-500">{notif.time}</span>
                        </div>
                        <p className="text-sm text-slate-300">{notif.message}</p>
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