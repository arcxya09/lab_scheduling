namespace LabSchedule.Core;

public static class SampleSchedule
{
    private const string Json = """
        {
          "year": 2026,
          "title": "2026年度示例实验安排",
          "settings": {
            "page_size": "A4",
            "orientation": "landscape",
            "font": "SimHei",
            "show_legend": true,
            "show_weekends": false,
            "show_maintenance_in_list": true
          },
          "items": [
            { "id": "EXP001", "type": "experiment", "name": "番茄种植观察", "leader": "张三", "start_date": "2026-06-01", "end_date": "2026-06-02", "color": "#f28e2b", "description": "温室 2 号" },
            { "id": "EXP002", "type": "experiment", "name": "面包发酵对比", "leader": "李四", "start_date": "2026-06-03", "end_date": "2026-06-10", "color": "#59a14f", "description": "" },
            { "id": "EXP003", "type": "experiment", "name": "纸飞机飞行测试", "leader": "王五/张三", "start_date": "2026-06-11", "end_date": "2026-06-15", "color": "#59a14f", "description": "" },
            { "id": "MAINT001", "type": "maintenance", "name": "空调检修/线路整理/门窗更换", "leader": "赵六", "start_date": "2026-06-16", "end_date": "2026-07-31", "color": "#B0B0B0", "description": "" },
            { "id": "EXP004", "type": "experiment", "name": "咖啡萃取对比", "leader": "李四/钱七", "start_date": "2026-08-01", "end_date": "2026-08-05", "color": "#76b7b2", "description": "" },
            { "id": "EXP005", "type": "experiment", "name": "肥皂泡寿命测量", "leader": "孙八", "start_date": "2026-08-06", "end_date": "2026-08-11", "color": "#edc948", "description": "需要高速相机" },
            { "id": "EXP006", "type": "experiment", "name": "盆栽浇水周期", "leader": "周九", "start_date": "2026-08-12", "end_date": "2026-08-23", "color": "#b07aa1", "description": "" },
            { "id": "EXP007", "type": "experiment", "name": "风筝稳定性测试", "leader": "吴十/郑十一", "start_date": "2026-08-24", "end_date": "2026-09-06", "color": "#ff9da7", "description": "" },
            { "id": "EXP008", "type": "experiment", "name": "冰块融化速度", "leader": "冯十二", "start_date": "2026-09-07", "end_date": "2026-09-20", "color": "#9c755f", "description": "需要预约场地" },
            { "id": "EXP009", "type": "experiment", "name": "纸桥承重测试", "leader": "陈十三", "start_date": "2026-09-21", "end_date": "2026-09-30", "color": "#bab0ac", "description": "需要预约场地" },
            { "id": "EXP010", "type": "experiment", "name": "回形针磁化观察", "leader": "张三", "start_date": "2026-10-09", "end_date": "2026-10-17", "color": "#4e79a7", "description": "" },
            { "id": "EXP011", "type": "experiment", "name": "雨水收集统计", "leader": "褚十四/冯十二", "start_date": "2026-10-18", "end_date": "2026-10-25", "color": "#59a14f", "description": "需要预约场地" },
            { "id": "EXP012", "type": "experiment", "name": "蚂蚁路径记录", "leader": "冯十二", "start_date": "2026-10-26", "end_date": "2026-10-31", "color": "#e15759", "description": "" }
          ]
        }
        """;

    /// <summary>The raw sample, before normalisation (colours keep their original casing).</summary>
    public static System.Text.Json.Nodes.JsonNode CreateRaw() => ScheduleJson.Parse(Json)!;
}
