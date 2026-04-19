using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Wayfarer.Core.Models;

namespace Wayfarer.Worker.Config;

public sealed class SqlitePmSnapshotStore
{
    private readonly ILogger<SqlitePmSnapshotStore> _logger;
    private readonly string _connectionString;

    public SqlitePmSnapshotStore(ILogger<SqlitePmSnapshotStore> logger)
    {
        _logger = logger;

        var dbPath = Path.Combine(AppContext.BaseDirectory, "wayfarer.db");

        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared
        }.ToString();
    }

    public async Task SaveSnapshotsAsync(
        IReadOnlyList<PmWoRecord> records,
        CancellationToken cancellationToken = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);
        await ConfigureConnectionAsync(conn, cancellationToken);

        await using var tx = conn.BeginTransaction();

        try
        {
            await EnsureIndexTableAsync(conn, tx, cancellationToken);
            await EnsureDetailTablesAsync(conn, tx, cancellationToken);

            // สำคัญ: ลบ child ก่อน parent
            await DeleteAllDetailTablesAsync(conn, tx, cancellationToken);
            await DeleteAllIndexAsync(conn, tx, cancellationToken);

            await InsertAllIndexAsync(conn, tx, records, cancellationToken);

            await tx.CommitAsync(cancellationToken);
        }
        catch
        {
            await tx.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task<IReadOnlyList<PmWoRecord>> LoadIndexRecordsAsync(
        CancellationToken cancellationToken = default)
    {
        var result = new List<PmWoRecord>();

        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);
        await ConfigureConnectionAsync(conn, cancellationToken);

        const string sql = """
        SELECT
            wo_no,
            detail_url,
            wo_code,
            wo_date,
            wo_problem,
            wo_status_no,
            wo_status_code,
            wo_type_code,
            eq_no,
            pu_no,
            dept_code,
            fetched_at_utc
        FROM pm_wo_index
        ORDER BY wo_date DESC, wo_no DESC;
        """;

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new PmWoRecord
            {
                WoNo = reader.GetInt32(0),
                DetailUrl = reader.IsDBNull(1) ? null : reader.GetString(1),
                WoCode = reader.IsDBNull(2) ? null : reader.GetString(2),
                WoDate = reader.IsDBNull(3) ? null : reader.GetString(3),
                WoProblem = reader.IsDBNull(4) ? null : reader.GetString(4),
                WoStatusNo = reader.IsDBNull(5) ? 0 : reader.GetInt32(5),
                WoStatusCode = reader.IsDBNull(6) ? null : reader.GetString(6),
                WoTypeCode = reader.IsDBNull(7) ? null : reader.GetString(7),
                EqNo = reader.IsDBNull(8) ? 0 : reader.GetInt32(8),
                PuNo = reader.IsDBNull(9) ? 0 : reader.GetInt32(9),
                DeptCode = reader.IsDBNull(10) ? null : reader.GetString(10),
                FetchedAtUtc = reader.IsDBNull(11) ? null : reader.GetString(11)
            });
        }

        return result;
    }

    public async Task SaveDetailPayloadsAsync(
        IReadOnlyList<PmWoDetailEnvelope> payloads,
        CancellationToken cancellationToken = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);
        await ConfigureConnectionAsync(conn, cancellationToken);

        await using var tx = conn.BeginTransaction();

        try
        {
            await EnsureDetailTablesAsync(conn, tx, cancellationToken);
            await DeleteAllDetailTablesAsync(conn, tx, cancellationToken);

            foreach (var payload in payloads)
            {
                using var doc = JsonDocument.Parse(payload.Json);
                var data = doc.RootElement.GetProperty("data");

                await InsertOverviewAsync(conn, tx, payload, data, cancellationToken);
                await InsertScheduleStatusAsync(conn, tx, payload, data, cancellationToken);
                await InsertPeopleDepartmentsAsync(conn, tx, payload, data, cancellationToken);
                await InsertDamageFailureAsync(conn, tx, payload, data, cancellationToken);
                await InsertHistoryAsync(conn, tx, payload, data, cancellationToken);
                await InsertTaskAsync(conn, tx, payload, data, cancellationToken);
                await InsertActualManhrsAsync(conn, tx, payload, data, cancellationToken);
                await InsertMetaFlagsAsync(conn, tx, payload, data, cancellationToken);
                await InsertFlowTypeAsync(conn, tx, payload, data, cancellationToken);
            }

            await tx.CommitAsync(cancellationToken);
        }
        catch
        {
            await tx.RollbackAsync(cancellationToken);
            throw;
        }
    }

    private static async Task ConfigureConnectionAsync(
        SqliteConnection conn,
        CancellationToken cancellationToken)
    {
        await ExecAsync(conn, "PRAGMA journal_mode=WAL;", cancellationToken);
        await ExecAsync(conn, "PRAGMA synchronous=NORMAL;", cancellationToken);
        await ExecAsync(conn, "PRAGMA busy_timeout=5000;", cancellationToken);
        await ExecAsync(conn, "PRAGMA foreign_keys=ON;", cancellationToken);
    }

    private static async Task ExecAsync(
        SqliteConnection conn,
        string sql,
        CancellationToken cancellationToken)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task EnsureIndexTableAsync(
        SqliteConnection conn,
        SqliteTransaction tx,
        CancellationToken cancellationToken)
    {
        const string sql = """
        CREATE TABLE IF NOT EXISTS pm_wo_index (
            wo_no           INTEGER PRIMARY KEY,
            detail_url      TEXT,
            wo_code         TEXT,
            wo_date         TEXT,
            wo_problem      TEXT,
            wo_status_no    INTEGER,
            wo_status_code  TEXT,
            wo_type_code    TEXT,
            eq_no           INTEGER,
            pu_no           INTEGER,
            dept_code       TEXT,
            fetched_at_utc  TEXT
        );
        """;

        await using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task EnsureDetailTablesAsync(
        SqliteConnection conn,
        SqliteTransaction tx,
        CancellationToken cancellationToken)
    {
        var sqlList = new[]
        {
            """
            CREATE TABLE IF NOT EXISTS pm_wo_overview (
                wo_no INTEGER PRIMARY KEY,
                wr_no INTEGER,
                wr_code TEXT,
                wr_date TEXT,
                wr_time TEXT,
                target TEXT,
                repair_by TEXT,
                cost_center_no INTEGER,
                dept_no INTEGER,
                req_dept_no INTEGER,
                pu_no INTEGER,
                eq_no INTEGER,
                wo_type_no INTEGER,
                wo_sub_type_no INTEGER,
                priority_no INTEGER,
                wf_step_approve_no TEXT,
                site_no INTEGER,
                eq_serial_no TEXT,
                assign_person_no INTEGER,
                work_by_person_no INTEGER,
                note TEXT,
                remark TEXT,
                fetched_at_utc TEXT,
                FOREIGN KEY (wo_no) REFERENCES pm_wo_index(wo_no)
            );
            """,
            """
            CREATE TABLE IF NOT EXISTS pm_wo_schedule_status (
                wo_no INTEGER PRIMARY KEY,
                sch_start_d TEXT,
                sch_start_t TEXT,
                sch_finish_d TEXT,
                sch_finish_t TEXT,
                sch_duration INTEGER,
                act_start_d TEXT,
                act_start_t TEXT,
                act_finish_d TEXT,
                act_finish_t TEXT,
                act_duration INTEGER,
                work_duration INTEGER,
                dt_start_d TEXT,
                dt_start_t TEXT,
                dt_finish_d TEXT,
                dt_finish_t TEXT,
                dt_duration INTEGER,
                complete_date TEXT,
                complete_time TEXT,
                accept_date TEXT,
                accept_time TEXT,
                cancel_date TEXT,
                cancel_time TEXT,
                wo_status_no INTEGER,
                wo_status_code TEXT,
                wo_status_name TEXT,
                fetched_at_utc TEXT,
                FOREIGN KEY (wo_no) REFERENCES pm_wo_index(wo_no)
            );
            """,
            """
            CREATE TABLE IF NOT EXISTS pm_wo_people_departments (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                wo_no INTEGER,
                role_type TEXT,
                person_no INTEGER,
                person_code TEXT,
                person_name TEXT,
                dept_no INTEGER,
                dept_code TEXT,
                dept_name TEXT,
                costcenter_no INTEGER,
                costcenter_code TEXT,
                costcenter_name TEXT,
                site_no INTEGER,
                site_code TEXT,
                site_name TEXT,
                fetched_at_utc TEXT,
                FOREIGN KEY (wo_no) REFERENCES pm_wo_index(wo_no)
            );
            """,
            """
            CREATE TABLE IF NOT EXISTS pm_wo_damage_failure (
                wo_no INTEGER PRIMARY KEY,
                damage_no INTEGER,
                damage_code TEXT,
                damage_name TEXT,
                failure_mode_no INTEGER,
                failure_mode_code TEXT,
                failure_mode_name TEXT,
                failure_cause_no INTEGER,
                failure_cause_code TEXT,
                failure_cause_name TEXT,
                failure_action_no INTEGER,
                failure_action_code TEXT,
                failure_action_name TEXT,
                component TEXT,
                effect_desc TEXT,
                cause_desc TEXT,
                action_desc TEXT,
                other_problem TEXT,
                other_cause TEXT,
                other_action TEXT,
                other_action_result TEXT,
                fetched_at_utc TEXT,
                FOREIGN KEY (wo_no) REFERENCES pm_wo_index(wo_no)
            );
            """,
            """
            CREATE TABLE IF NOT EXISTS pm_wo_history (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                wo_no INTEGER,
                seq_no INTEGER,
                type TEXT,
                detail TEXT,
                timestamps TEXT,
                action_person_no INTEGER,
                action_person_code TEXT,
                action_person_name TEXT,
                fetched_at_utc TEXT,
                FOREIGN KEY (wo_no) REFERENCES pm_wo_index(wo_no)
            );
            """,
            """
            CREATE TABLE IF NOT EXISTS pm_wo_task (
                wo_task_no INTEGER PRIMARY KEY,
                wo_no INTEGER,
                task_order INTEGER,
                task_name TEXT,
                task_procedure TEXT,
                task_duration INTEGER,
                remark TEXT,
                wo_cause TEXT,
                task_date TEXT,
                task_done INTEGER,
                pu_no INTEGER,
                pu_code TEXT,
                pu_name TEXT,
                eq_no INTEGER,
                eq_code TEXT,
                eq_name TEXT,
                failure_action_no INTEGER,
                failure_action_code TEXT,
                failure_action_name TEXT,
                failure_mode_no INTEGER,
                failure_mode_code TEXT,
                failure_mode_name TEXT,
                failure_cause_no INTEGER,
                failure_cause_code TEXT,
                failure_cause_name TEXT,
                fetched_at_utc TEXT,
                FOREIGN KEY (wo_no) REFERENCES pm_wo_index(wo_no)
            );
            """,
            """
            CREATE TABLE IF NOT EXISTS pm_wo_actual_manhrs (
                wo_resc_no INTEGER PRIMARY KEY,
                wo_no INTEGER,
                task_no INTEGER,
                hours INTEGER,
                qty INTEGER,
                unit TEXT,
                qty_hours REAL,
                amount REAL,
                unit_cost REAL,
                flag_act INTEGER,
                tr_date TEXT,
                person_no INTEGER,
                person_code TEXT,
                person_name TEXT,
                rate_person REAL,
                dept_no INTEGER,
                dept_code TEXT,
                dept_name TEXT,
                fetched_at_utc TEXT,
                FOREIGN KEY (wo_no) REFERENCES pm_wo_index(wo_no)
            );
            """,
            """
            CREATE TABLE IF NOT EXISTS pm_wo_meta_flags (
                wo_no INTEGER PRIMARY KEY,
                hot_work INTEGER,
                confine_space INTEGER,
                work_at_height INTEGER,
                lock_out_tag_out INTEGER,
                wait_for_shutdown INTEGER,
                wait_for_material INTEGER,
                wait_for_other INTEGER,
                flag_cancel INTEGER,
                flag_his INTEGER,
                flag_del INTEGER,
                flag_approve_m INTEGER,
                flag_approve_resc INTEGER,
                flag_approve INTEGER,
                flag_not_approved INTEGER,
                flag_wait_status INTEGER,
                flag_pu INTEGER,
                print_flag INTEGER,
                authorize_csv TEXT,
                fetched_at_utc TEXT,
                FOREIGN KEY (wo_no) REFERENCES pm_wo_index(wo_no)
            );
            """,
            """
            CREATE TABLE IF NOT EXISTS pm_wo_flowtype (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                wo_no INTEGER,
                current_status INTEGER,
                next_status INTEGER,
                wf_current INTEGER,
                wf_next INTEGER,
                flow_site_no INTEGER,
                flow_wo_type TEXT,
                flow_type_csv TEXT,
                wf_csv TEXT,
                fetched_at_utc TEXT,
                FOREIGN KEY (wo_no) REFERENCES pm_wo_index(wo_no)
            );
            """
        };

        foreach (var sql in sqlList)
        {
            await using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = sql;
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static async Task DeleteAllIndexAsync(
        SqliteConnection conn,
        SqliteTransaction tx,
        CancellationToken cancellationToken)
    {
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "DELETE FROM pm_wo_index;";
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task DeleteAllDetailTablesAsync(
        SqliteConnection conn,
        SqliteTransaction tx,
        CancellationToken cancellationToken)
    {
        var deletes = new[]
        {
            "DELETE FROM pm_wo_overview;",
            "DELETE FROM pm_wo_schedule_status;",
            "DELETE FROM pm_wo_people_departments;",
            "DELETE FROM pm_wo_damage_failure;",
            "DELETE FROM pm_wo_history;",
            "DELETE FROM pm_wo_task;",
            "DELETE FROM pm_wo_actual_manhrs;",
            "DELETE FROM pm_wo_meta_flags;",
            "DELETE FROM pm_wo_flowtype;"
        };

        foreach (var sql in deletes)
        {
            await using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = sql;
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static async Task InsertAllIndexAsync(
        SqliteConnection conn,
        SqliteTransaction tx,
        IReadOnlyList<PmWoRecord> records,
        CancellationToken cancellationToken)
    {
        const string sql = """
        INSERT INTO pm_wo_index (
            wo_no, detail_url, wo_code, wo_date, wo_problem,
            wo_status_no, wo_status_code, wo_type_code,
            eq_no, pu_no, dept_code, fetched_at_utc
        )
        VALUES (
            $wo_no, $detail_url, $wo_code, $wo_date, $wo_problem,
            $wo_status_no, $wo_status_code, $wo_type_code,
            $eq_no, $pu_no, $dept_code, $fetched_at_utc
        );
        """;

        foreach (var r in records)
        {
            await using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = sql;

            cmd.Parameters.AddWithValue("$wo_no", r.WoNo);
            cmd.Parameters.AddWithValue("$detail_url", Db(r.DetailUrl));
            cmd.Parameters.AddWithValue("$wo_code", Db(r.WoCode));
            cmd.Parameters.AddWithValue("$wo_date", Db(r.WoDate));
            cmd.Parameters.AddWithValue("$wo_problem", Db(r.WoProblem));
            cmd.Parameters.AddWithValue("$wo_status_no", r.WoStatusNo);
            cmd.Parameters.AddWithValue("$wo_status_code", Db(r.WoStatusCode));
            cmd.Parameters.AddWithValue("$wo_type_code", Db(r.WoTypeCode));
            cmd.Parameters.AddWithValue("$eq_no", r.EqNo);
            cmd.Parameters.AddWithValue("$pu_no", r.PuNo);
            cmd.Parameters.AddWithValue("$dept_code", Db(r.DeptCode));
            cmd.Parameters.AddWithValue("$fetched_at_utc", Db(r.FetchedAtUtc));

            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static async Task InsertOverviewAsync(
        SqliteConnection conn,
        SqliteTransaction tx,
        PmWoDetailEnvelope payload,
        JsonElement data,
        CancellationToken cancellationToken)
    {
        const string sql = """
        INSERT INTO pm_wo_overview (
            wo_no, wr_no, wr_code, wr_date, wr_time, target, repair_by,
            cost_center_no, dept_no, req_dept_no, pu_no, eq_no,
            wo_type_no, wo_sub_type_no, priority_no, wf_step_approve_no,
            site_no, eq_serial_no, assign_person_no, work_by_person_no,
            note, remark, fetched_at_utc
        )
        VALUES (
            $wo_no, $wr_no, $wr_code, $wr_date, $wr_time, $target, $repair_by,
            $cost_center_no, $dept_no, $req_dept_no, $pu_no, $eq_no,
            $wo_type_no, $wo_sub_type_no, $priority_no, $wf_step_approve_no,
            $site_no, $eq_serial_no, $assign_person_no, $work_by_person_no,
            $note, $remark, $fetched_at_utc
        );
        """;

        await using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;

        cmd.Parameters.AddWithValue("$wo_no", payload.WoNo);
        cmd.Parameters.AddWithValue("$wr_no", Db(GetInt(data, "wrNo")));
        cmd.Parameters.AddWithValue("$wr_code", Db(GetString(data, "wrCode")));
        cmd.Parameters.AddWithValue("$wr_date", Db(GetString(data, "wrDate")));
        cmd.Parameters.AddWithValue("$wr_time", Db(GetString(data, "wrTime")));
        cmd.Parameters.AddWithValue("$target", Db(GetString(data, "target")));
        cmd.Parameters.AddWithValue("$repair_by", Db(GetString(data, "repairBy")));
        cmd.Parameters.AddWithValue("$cost_center_no", Db(GetInt(data, "costCenterNo")));
        cmd.Parameters.AddWithValue("$dept_no", Db(GetInt(data, "deptNo")));
        cmd.Parameters.AddWithValue("$req_dept_no", Db(GetInt(data, "reqDeptNo")));
        cmd.Parameters.AddWithValue("$pu_no", Db(GetInt(data, "puNo")));
        cmd.Parameters.AddWithValue("$eq_no", Db(GetInt(data, "eqNo")));
        cmd.Parameters.AddWithValue("$wo_type_no", Db(GetInt(data, "woTypeNo")));
        cmd.Parameters.AddWithValue("$wo_sub_type_no", Db(GetInt(data, "woSubTypeNo")));
        cmd.Parameters.AddWithValue("$priority_no", Db(GetInt(data, "priorityNo")));
        cmd.Parameters.AddWithValue("$wf_step_approve_no", Db(GetString(data, "wfStepApproveNo")));
        cmd.Parameters.AddWithValue("$site_no", Db(GetInt(data, "siteNo")));
        cmd.Parameters.AddWithValue("$eq_serial_no", Db(GetString(data, "eqSerialNo")));
        cmd.Parameters.AddWithValue("$assign_person_no", Db(GetInt(data, "assign")));
        cmd.Parameters.AddWithValue("$work_by_person_no", Db(GetInt(data, "workBy")));
        cmd.Parameters.AddWithValue("$note", Db(GetString(data, "note")));
        cmd.Parameters.AddWithValue("$remark", Db(GetString(data, "remark")));
        cmd.Parameters.AddWithValue("$fetched_at_utc", Db(payload.FetchedAtUtc));

        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InsertScheduleStatusAsync(
        SqliteConnection conn,
        SqliteTransaction tx,
        PmWoDetailEnvelope payload,
        JsonElement data,
        CancellationToken cancellationToken)
    {
        const string sql = """
        INSERT INTO pm_wo_schedule_status (
            wo_no, sch_start_d, sch_start_t, sch_finish_d, sch_finish_t, sch_duration,
            act_start_d, act_start_t, act_finish_d, act_finish_t, act_duration, work_duration,
            dt_start_d, dt_start_t, dt_finish_d, dt_finish_t, dt_duration,
            complete_date, complete_time, accept_date, accept_time, cancel_date, cancel_time,
            wo_status_no, wo_status_code, wo_status_name, fetched_at_utc
        )
        VALUES (
            $wo_no, $sch_start_d, $sch_start_t, $sch_finish_d, $sch_finish_t, $sch_duration,
            $act_start_d, $act_start_t, $act_finish_d, $act_finish_t, $act_duration, $work_duration,
            $dt_start_d, $dt_start_t, $dt_finish_d, $dt_finish_t, $dt_duration,
            $complete_date, $complete_time, $accept_date, $accept_time, $cancel_date, $cancel_time,
            $wo_status_no, $wo_status_code, $wo_status_name, $fetched_at_utc
        );
        """;

        var status = GetObject(data, "status");

        await using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;

        cmd.Parameters.AddWithValue("$wo_no", payload.WoNo);
        cmd.Parameters.AddWithValue("$sch_start_d", Db(GetString(data, "schStartD")));
        cmd.Parameters.AddWithValue("$sch_start_t", Db(GetString(data, "schStartT")));
        cmd.Parameters.AddWithValue("$sch_finish_d", Db(GetString(data, "schFinishD")));
        cmd.Parameters.AddWithValue("$sch_finish_t", Db(GetString(data, "schFinishT")));
        cmd.Parameters.AddWithValue("$sch_duration", Db(GetInt(data, "schDuration")));
        cmd.Parameters.AddWithValue("$act_start_d", Db(GetString(data, "actStartD")));
        cmd.Parameters.AddWithValue("$act_start_t", Db(GetString(data, "actStartT")));
        cmd.Parameters.AddWithValue("$act_finish_d", Db(GetString(data, "actFinishD")));
        cmd.Parameters.AddWithValue("$act_finish_t", Db(GetString(data, "actFinishT")));
        cmd.Parameters.AddWithValue("$act_duration", Db(GetInt(data, "actDuration")));
        cmd.Parameters.AddWithValue("$work_duration", Db(GetInt(data, "workDuration")));
        cmd.Parameters.AddWithValue("$dt_start_d", Db(GetString(data, "dtStartD")));
        cmd.Parameters.AddWithValue("$dt_start_t", Db(GetString(data, "dtStartT")));
        cmd.Parameters.AddWithValue("$dt_finish_d", Db(GetString(data, "dtFinishD")));
        cmd.Parameters.AddWithValue("$dt_finish_t", Db(GetString(data, "dtFinishT")));
        cmd.Parameters.AddWithValue("$dt_duration", Db(GetInt(data, "dtDuration")));
        cmd.Parameters.AddWithValue("$complete_date", Db(GetString(data, "completeDate")));
        cmd.Parameters.AddWithValue("$complete_time", Db(GetString(data, "completeTime")));
        cmd.Parameters.AddWithValue("$accept_date", Db(GetString(data, "acceptDate")));
        cmd.Parameters.AddWithValue("$accept_time", Db(GetString(data, "acceptTime")));
        cmd.Parameters.AddWithValue("$cancel_date", Db(GetString(data, "cancelDate")));
        cmd.Parameters.AddWithValue("$cancel_time", Db(GetString(data, "cancelTime")));
        cmd.Parameters.AddWithValue("$wo_status_no", Db(GetInt(data, "woStatusNo")));
        cmd.Parameters.AddWithValue("$wo_status_code", Db(GetString(status, "woStatusCode")));
        cmd.Parameters.AddWithValue("$wo_status_name", Db(GetString(status, "woStatusName")));
        cmd.Parameters.AddWithValue("$fetched_at_utc", Db(payload.FetchedAtUtc));

        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InsertPeopleDepartmentsAsync(
        SqliteConnection conn,
        SqliteTransaction tx,
        PmWoDetailEnvelope payload,
        JsonElement data,
        CancellationToken cancellationToken)
    {
        await InsertPeopleRoleAsync(conn, tx, payload, "request_person", GetFirstArrayItem(data, "person"), cancellationToken);
        await InsertPeopleRoleAsync(conn, tx, payload, "assign_person", GetFirstArrayItem(data, "assignPerson"), cancellationToken);
        await InsertPeopleRoleAsync(conn, tx, payload, "work_by_person", GetObject(data, "workByPerson"), cancellationToken);
        await InsertDeptRoleAsync(conn, tx, payload, "maintenance_dept", GetObject(data, "maintenance_dept"), cancellationToken);
        await InsertDeptRoleAsync(conn, tx, payload, "req_dept", GetObject(data, "reqDept"), cancellationToken);
        await InsertCostcenterRoleAsync(conn, tx, payload, "costcenter", GetObject(data, "costcenter"), cancellationToken);
        await InsertSiteRoleAsync(conn, tx, payload, "site", GetObject(data, "site"), cancellationToken);
    }

    private static async Task InsertDamageFailureAsync(
        SqliteConnection conn,
        SqliteTransaction tx,
        PmWoDetailEnvelope payload,
        JsonElement data,
        CancellationToken cancellationToken)
    {
        const string sql = """
        INSERT INTO pm_wo_damage_failure (
            wo_no, damage_no, damage_code, damage_name,
            failure_mode_no, failure_mode_code, failure_mode_name,
            failure_cause_no, failure_cause_code, failure_cause_name,
            failure_action_no, failure_action_code, failure_action_name,
            component, effect_desc, cause_desc, action_desc,
            other_problem, other_cause, other_action, other_action_result,
            fetched_at_utc
        )
        VALUES (
            $wo_no, $damage_no, $damage_code, $damage_name,
            $failure_mode_no, $failure_mode_code, $failure_mode_name,
            $failure_cause_no, $failure_cause_code, $failure_cause_name,
            $failure_action_no, $failure_action_code, $failure_action_name,
            $component, $effect_desc, $cause_desc, $action_desc,
            $other_problem, $other_cause, $other_action, $other_action_result,
            $fetched_at_utc
        );
        """;

        var damage = GetObject(data, "damage");
        var failureMode = GetObject(data, "failureMode");
        var failureCause = GetObject(data, "failureCause");
        var failureAction = GetObject(data, "failureAction");

        await using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;

        cmd.Parameters.AddWithValue("$wo_no", payload.WoNo);
        cmd.Parameters.AddWithValue("$damage_no", Db(GetInt(damage, "woProblemNo")));
        cmd.Parameters.AddWithValue("$damage_code", Db(GetString(damage, "woProblemCode")));
        cmd.Parameters.AddWithValue("$damage_name", Db(GetString(damage, "woProblemName")));
        cmd.Parameters.AddWithValue("$failure_mode_no", Db(GetInt(failureMode, "failureModeNo")));
        cmd.Parameters.AddWithValue("$failure_mode_code", Db(GetString(failureMode, "failureModeCode")));
        cmd.Parameters.AddWithValue("$failure_mode_name", Db(GetString(failureMode, "failureModeName")));
        cmd.Parameters.AddWithValue("$failure_cause_no", Db(GetInt(failureCause, "failureCauseNo")));
        cmd.Parameters.AddWithValue("$failure_cause_code", Db(GetString(failureCause, "failureCauseCode")));
        cmd.Parameters.AddWithValue("$failure_cause_name", Db(GetString(failureCause, "failureCauseName")));
        cmd.Parameters.AddWithValue("$failure_action_no", Db(GetInt(failureAction, "failureActionNo")));
        cmd.Parameters.AddWithValue("$failure_action_code", Db(GetString(failureAction, "failureActionCode")));
        cmd.Parameters.AddWithValue("$failure_action_name", Db(GetString(failureAction, "failureActionName")));
        cmd.Parameters.AddWithValue("$component", Db(GetString(data, "component")));
        cmd.Parameters.AddWithValue("$effect_desc", Db(GetString(data, "effectDesc")));
        cmd.Parameters.AddWithValue("$cause_desc", Db(GetString(data, "causeDesc")));
        cmd.Parameters.AddWithValue("$action_desc", Db(GetString(data, "actionDesc")));
        cmd.Parameters.AddWithValue("$other_problem", Db(GetString(data, "otherProblem")));
        cmd.Parameters.AddWithValue("$other_cause", Db(GetString(data, "otherCause")));
        cmd.Parameters.AddWithValue("$other_action", Db(GetString(data, "otherAction")));
        cmd.Parameters.AddWithValue("$other_action_result", Db(GetString(data, "otherActionResult")));
        cmd.Parameters.AddWithValue("$fetched_at_utc", Db(payload.FetchedAtUtc));

        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InsertHistoryAsync(
        SqliteConnection conn,
        SqliteTransaction tx,
        PmWoDetailEnvelope payload,
        JsonElement data,
        CancellationToken cancellationToken)
    {
        if (!data.TryGetProperty("history", out var arr) || arr.ValueKind != JsonValueKind.Array)
            return;

        const string sql = """
        INSERT INTO pm_wo_history (
            wo_no, seq_no, type, detail, timestamps,
            action_person_no, action_person_code, action_person_name,
            fetched_at_utc
        )
        VALUES (
            $wo_no, $seq_no, $type, $detail, $timestamps,
            $action_person_no, $action_person_code, $action_person_name,
            $fetched_at_utc
        );
        """;

        var seq = 1;
        foreach (var item in arr.EnumerateArray())
        {
            var action = GetObject(item, "action");

            await using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = sql;

            cmd.Parameters.AddWithValue("$wo_no", payload.WoNo);
            cmd.Parameters.AddWithValue("$seq_no", seq++);
            cmd.Parameters.AddWithValue("$type", Db(GetString(item, "type")));
            cmd.Parameters.AddWithValue("$detail", Db(GetString(item, "detail")));
            cmd.Parameters.AddWithValue("$timestamps", Db(GetString(item, "timestamps")));
            cmd.Parameters.AddWithValue("$action_person_no", Db(GetInt(action, "personNo")));
            cmd.Parameters.AddWithValue("$action_person_code", Db(GetString(action, "personCode")));
            cmd.Parameters.AddWithValue("$action_person_name", Db(GetString(action, "personName")));
            cmd.Parameters.AddWithValue("$fetched_at_utc", Db(payload.FetchedAtUtc));

            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static async Task InsertTaskAsync(
        SqliteConnection conn,
        SqliteTransaction tx,
        PmWoDetailEnvelope payload,
        JsonElement data,
        CancellationToken cancellationToken)
    {
        if (!data.TryGetProperty("wo_task", out var arr) || arr.ValueKind != JsonValueKind.Array)
            return;

        const string sql = """
        INSERT INTO pm_wo_task (
            wo_task_no, wo_no, task_order, task_name, task_procedure, task_duration,
            remark, wo_cause, task_date, task_done,
            pu_no, pu_code, pu_name, eq_no, eq_code, eq_name,
            failure_action_no, failure_action_code, failure_action_name,
            failure_mode_no, failure_mode_code, failure_mode_name,
            failure_cause_no, failure_cause_code, failure_cause_name,
            fetched_at_utc
        )
        VALUES (
            $wo_task_no, $wo_no, $task_order, $task_name, $task_procedure, $task_duration,
            $remark, $wo_cause, $task_date, $task_done,
            $pu_no, $pu_code, $pu_name, $eq_no, $eq_code, $eq_name,
            $failure_action_no, $failure_action_code, $failure_action_name,
            $failure_mode_no, $failure_mode_code, $failure_mode_name,
            $failure_cause_no, $failure_cause_code, $failure_cause_name,
            $fetched_at_utc
        );
        """;

        foreach (var item in arr.EnumerateArray())
        {
            var pu = GetObject(item, "pu");
            var eq = GetObject(item, "eq");
            var failureAction = GetObject(item, "failureAction");
            var failureMode = GetObject(item, "failureMode");
            var failureCause = GetObject(item, "failureCause");

            await using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = sql;

            cmd.Parameters.AddWithValue("$wo_task_no", GetInt(item, "woTaskNo") ?? 0);
            cmd.Parameters.AddWithValue("$wo_no", payload.WoNo);
            cmd.Parameters.AddWithValue("$task_order", Db(GetInt(item, "taskOrder")));
            cmd.Parameters.AddWithValue("$task_name", Db(GetString(item, "taskName")));
            cmd.Parameters.AddWithValue("$task_procedure", Db(GetString(item, "taskProcedure")));
            cmd.Parameters.AddWithValue("$task_duration", Db(GetInt(item, "taskDuration")));
            cmd.Parameters.AddWithValue("$remark", Db(GetString(item, "remark")));
            cmd.Parameters.AddWithValue("$wo_cause", Db(GetString(item, "woCause")));
            cmd.Parameters.AddWithValue("$task_date", Db(GetString(item, "taskDate")));
            cmd.Parameters.AddWithValue("$task_done", Db(GetBoolInt(item, "taskDone")));
            cmd.Parameters.AddWithValue("$pu_no", Db(GetInt(pu, "puNo")));
            cmd.Parameters.AddWithValue("$pu_code", Db(GetString(pu, "puCode")));
            cmd.Parameters.AddWithValue("$pu_name", Db(GetString(pu, "puName")));
            cmd.Parameters.AddWithValue("$eq_no", Db(GetInt(eq, "eqNo")));
            cmd.Parameters.AddWithValue("$eq_code", Db(GetString(eq, "eqCode")));
            cmd.Parameters.AddWithValue("$eq_name", Db(GetString(eq, "eqName")));
            cmd.Parameters.AddWithValue("$failure_action_no", Db(GetInt(failureAction, "failureActionNo")));
            cmd.Parameters.AddWithValue("$failure_action_code", Db(GetString(failureAction, "failureActionCode")));
            cmd.Parameters.AddWithValue("$failure_action_name", Db(GetString(failureAction, "failureActionName")));
            cmd.Parameters.AddWithValue("$failure_mode_no", Db(GetInt(failureMode, "failureModeNo")));
            cmd.Parameters.AddWithValue("$failure_mode_code", Db(GetString(failureMode, "failureModeCode")));
            cmd.Parameters.AddWithValue("$failure_mode_name", Db(GetString(failureMode, "failureModeName")));
            cmd.Parameters.AddWithValue("$failure_cause_no", Db(GetInt(failureCause, "failureCauseNo")));
            cmd.Parameters.AddWithValue("$failure_cause_code", Db(GetString(failureCause, "failureCauseCode")));
            cmd.Parameters.AddWithValue("$failure_cause_name", Db(GetString(failureCause, "failureCauseName")));
            cmd.Parameters.AddWithValue("$fetched_at_utc", Db(payload.FetchedAtUtc));

            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static async Task InsertActualManhrsAsync(
        SqliteConnection conn,
        SqliteTransaction tx,
        PmWoDetailEnvelope payload,
        JsonElement data,
        CancellationToken cancellationToken)
    {
        if (!data.TryGetProperty("actual_manhrs", out var arr) || arr.ValueKind != JsonValueKind.Array)
            return;

        const string sql = """
        INSERT INTO pm_wo_actual_manhrs (
            wo_resc_no, wo_no, task_no, hours, qty, unit, qty_hours, amount, unit_cost, flag_act, tr_date,
            person_no, person_code, person_name, rate_person, dept_no, dept_code, dept_name, fetched_at_utc
        )
        VALUES (
            $wo_resc_no, $wo_no, $task_no, $hours, $qty, $unit, $qty_hours, $amount, $unit_cost, $flag_act, $tr_date,
            $person_no, $person_code, $person_name, $rate_person, $dept_no, $dept_code, $dept_name, $fetched_at_utc
        );
        """;

        foreach (var item in arr.EnumerateArray())
        {
            var person = GetObject(item, "person");
            var dept = GetObject(person, "department");

            await using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = sql;

            cmd.Parameters.AddWithValue("$wo_resc_no", GetInt(item, "woRescNo") ?? 0);
            cmd.Parameters.AddWithValue("$wo_no", payload.WoNo);
            cmd.Parameters.AddWithValue("$task_no", Db(GetInt(item, "taskNo")));
            cmd.Parameters.AddWithValue("$hours", Db(GetInt(item, "hours")));
            cmd.Parameters.AddWithValue("$qty", Db(GetInt(item, "qty")));
            cmd.Parameters.AddWithValue("$unit", Db(GetString(item, "unit")));
            cmd.Parameters.AddWithValue("$qty_hours", Db(GetDecimal(item, "qtyHours")));
            cmd.Parameters.AddWithValue("$amount", Db(GetDecimal(item, "amount")));
            cmd.Parameters.AddWithValue("$unit_cost", Db(GetDecimal(item, "unitCost")));
            cmd.Parameters.AddWithValue("$flag_act", Db(GetBoolInt(item, "flagAct")));
            cmd.Parameters.AddWithValue("$tr_date", Db(GetString(item, "trDate")));
            cmd.Parameters.AddWithValue("$person_no", Db(GetInt(person, "personNo")));
            cmd.Parameters.AddWithValue("$person_code", Db(GetString(person, "personCode")));
            cmd.Parameters.AddWithValue("$person_name", Db(GetString(person, "personName")));
            cmd.Parameters.AddWithValue("$rate_person", Db(GetDecimal(person, "rate_person")));
            cmd.Parameters.AddWithValue("$dept_no", Db(GetInt(dept, "deptNo")));
            cmd.Parameters.AddWithValue("$dept_code", Db(GetString(dept, "deptCode")));
            cmd.Parameters.AddWithValue("$dept_name", Db(GetString(dept, "deptName")));
            cmd.Parameters.AddWithValue("$fetched_at_utc", Db(payload.FetchedAtUtc));

            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static async Task InsertMetaFlagsAsync(
        SqliteConnection conn,
        SqliteTransaction tx,
        PmWoDetailEnvelope payload,
        JsonElement data,
        CancellationToken cancellationToken)
    {
        const string sql = """
        INSERT INTO pm_wo_meta_flags (
            wo_no, hot_work, confine_space, work_at_height, lock_out_tag_out,
            wait_for_shutdown, wait_for_material, wait_for_other,
            flag_cancel, flag_his, flag_del, flag_approve_m,
            flag_approve_resc, flag_approve, flag_not_approved,
            flag_wait_status, flag_pu, print_flag, authorize_csv, fetched_at_utc
        )
        VALUES (
            $wo_no, $hot_work, $confine_space, $work_at_height, $lock_out_tag_out,
            $wait_for_shutdown, $wait_for_material, $wait_for_other,
            $flag_cancel, $flag_his, $flag_del, $flag_approve_m,
            $flag_approve_resc, $flag_approve, $flag_not_approved,
            $flag_wait_status, $flag_pu, $print_flag, $authorize_csv, $fetched_at_utc
        );
        """;

        await using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;

        cmd.Parameters.AddWithValue("$wo_no", payload.WoNo);
        cmd.Parameters.AddWithValue("$hot_work", Db(GetBoolInt(data, "hotWork")));
        cmd.Parameters.AddWithValue("$confine_space", Db(GetBoolInt(data, "confineSpace")));
        cmd.Parameters.AddWithValue("$work_at_height", Db(GetBoolInt(data, "workAtHeight")));
        cmd.Parameters.AddWithValue("$lock_out_tag_out", Db(GetBoolInt(data, "lockOutTagOut")));
        cmd.Parameters.AddWithValue("$wait_for_shutdown", Db(GetBoolInt(data, "waitForShutDown")));
        cmd.Parameters.AddWithValue("$wait_for_material", Db(GetBoolInt(data, "waitForMaterial")));
        cmd.Parameters.AddWithValue("$wait_for_other", Db(GetBoolInt(data, "waitForOther")));
        cmd.Parameters.AddWithValue("$flag_cancel", Db(GetBoolInt(data, "flagCancel")));
        cmd.Parameters.AddWithValue("$flag_his", Db(GetBoolInt(data, "flagHis")));
        cmd.Parameters.AddWithValue("$flag_del", Db(GetBoolInt(data, "flagDel")));
        cmd.Parameters.AddWithValue("$flag_approve_m", Db(GetBoolInt(data, "flagApproveM")));
        cmd.Parameters.AddWithValue("$flag_approve_resc", Db(GetBoolInt(data, "flagApproveResc")));
        cmd.Parameters.AddWithValue("$flag_approve", Db(GetBoolInt(data, "flagApprove")));
        cmd.Parameters.AddWithValue("$flag_not_approved", Db(GetBoolInt(data, "flagNotApproved")));
        cmd.Parameters.AddWithValue("$flag_wait_status", Db(GetBoolInt(data, "flagWaitStatus")));
        cmd.Parameters.AddWithValue("$flag_pu", Db(GetBoolInt(data, "flagPu")));
        cmd.Parameters.AddWithValue("$print_flag", Db(GetBoolInt(data, "printFlag")));
        cmd.Parameters.AddWithValue("$authorize_csv", Db(GetStringArrayCsv(data, "authorize")));
        cmd.Parameters.AddWithValue("$fetched_at_utc", Db(payload.FetchedAtUtc));

        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InsertFlowTypeAsync(
        SqliteConnection conn,
        SqliteTransaction tx,
        PmWoDetailEnvelope payload,
        JsonElement data,
        CancellationToken cancellationToken)
    {
        if (!data.TryGetProperty("flowType", out var arr) || arr.ValueKind != JsonValueKind.Array)
            return;

        const string sql = """
        INSERT INTO pm_wo_flowtype (
            wo_no, current_status, next_status, wf_current, wf_next,
            flow_site_no, flow_wo_type, flow_type_csv, wf_csv, fetched_at_utc
        )
        VALUES (
            $wo_no, $current_status, $next_status, $wf_current, $wf_next,
            $flow_site_no, $flow_wo_type, $flow_type_csv, $wf_csv, $fetched_at_utc
        );
        """;

        foreach (var item in arr.EnumerateArray())
        {
            var flowType = GetObject(item, "flowType");

            await using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = sql;

            cmd.Parameters.AddWithValue("$wo_no", payload.WoNo);
            cmd.Parameters.AddWithValue("$current_status", Db(GetInt(item, "current")));
            cmd.Parameters.AddWithValue("$next_status", Db(GetInt(item, "next")));
            cmd.Parameters.AddWithValue("$wf_current", Db(GetInt(item, "wfCurrent")));
            cmd.Parameters.AddWithValue("$wf_next", Db(GetInt(item, "wfNext")));
            cmd.Parameters.AddWithValue("$flow_site_no", Db(GetInt(flowType, "siteNo")));
            cmd.Parameters.AddWithValue("$flow_wo_type", Db(GetString(flowType, "woType")));
            cmd.Parameters.AddWithValue("$flow_type_csv", Db(GetIntArrayCsv(flowType, "flowType")));
            cmd.Parameters.AddWithValue("$wf_csv", Db(GetIntArrayCsv(flowType, "wf")));
            cmd.Parameters.AddWithValue("$fetched_at_utc", Db(payload.FetchedAtUtc));

            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static async Task InsertPeopleRoleAsync(
        SqliteConnection conn,
        SqliteTransaction tx,
        PmWoDetailEnvelope payload,
        string roleType,
        JsonElement? person,
        CancellationToken cancellationToken)
    {
        if (person is null || person.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return;

        const string sql = """
        INSERT INTO pm_wo_people_departments (
            wo_no, role_type, person_no, person_code, person_name,
            dept_no, dept_code, dept_name, fetched_at_utc
        )
        VALUES (
            $wo_no, $role_type, $person_no, $person_code, $person_name,
            $dept_no, $dept_code, $dept_name, $fetched_at_utc
        );
        """;

        var dept = GetObject(person.Value, "department");

        await using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;

        cmd.Parameters.AddWithValue("$wo_no", payload.WoNo);
        cmd.Parameters.AddWithValue("$role_type", roleType);
        cmd.Parameters.AddWithValue("$person_no", Db(GetInt(person.Value, "personNo")));
        cmd.Parameters.AddWithValue("$person_code", Db(GetString(person.Value, "personCode")));
        cmd.Parameters.AddWithValue("$person_name", Db(GetString(person.Value, "personName")));
        cmd.Parameters.AddWithValue("$dept_no", Db(GetInt(dept, "deptNo")));
        cmd.Parameters.AddWithValue("$dept_code", Db(GetString(dept, "deptCode")));
        cmd.Parameters.AddWithValue("$dept_name", Db(GetString(dept, "deptName")));
        cmd.Parameters.AddWithValue("$fetched_at_utc", Db(payload.FetchedAtUtc));

        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InsertDeptRoleAsync(
        SqliteConnection conn,
        SqliteTransaction tx,
        PmWoDetailEnvelope payload,
        string roleType,
        JsonElement? dept,
        CancellationToken cancellationToken)
    {
        if (dept is null || dept.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return;

        const string sql = """
        INSERT INTO pm_wo_people_departments (
            wo_no, role_type, dept_no, dept_code, dept_name, fetched_at_utc
        )
        VALUES (
            $wo_no, $role_type, $dept_no, $dept_code, $dept_name, $fetched_at_utc
        );
        """;

        await using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;

        cmd.Parameters.AddWithValue("$wo_no", payload.WoNo);
        cmd.Parameters.AddWithValue("$role_type", roleType);
        cmd.Parameters.AddWithValue("$dept_no", Db(GetInt(dept.Value, "deptNo")));
        cmd.Parameters.AddWithValue("$dept_code", Db(GetString(dept.Value, "deptCode")));
        cmd.Parameters.AddWithValue("$dept_name", Db(GetString(dept.Value, "deptName")));
        cmd.Parameters.AddWithValue("$fetched_at_utc", Db(payload.FetchedAtUtc));

        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InsertCostcenterRoleAsync(
        SqliteConnection conn,
        SqliteTransaction tx,
        PmWoDetailEnvelope payload,
        string roleType,
        JsonElement? cc,
        CancellationToken cancellationToken)
    {
        if (cc is null || cc.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return;

        const string sql = """
        INSERT INTO pm_wo_people_departments (
            wo_no, role_type, costcenter_no, costcenter_code, costcenter_name, fetched_at_utc
        )
        VALUES (
            $wo_no, $role_type, $costcenter_no, $costcenter_code, $costcenter_name, $fetched_at_utc
        );
        """;

        await using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;

        cmd.Parameters.AddWithValue("$wo_no", payload.WoNo);
        cmd.Parameters.AddWithValue("$role_type", roleType);
        cmd.Parameters.AddWithValue("$costcenter_no", Db(GetInt(cc.Value, "costcenterNo")));
        cmd.Parameters.AddWithValue("$costcenter_code", Db(GetString(cc.Value, "costcenterCode")));
        cmd.Parameters.AddWithValue("$costcenter_name", Db(GetString(cc.Value, "costcenterName")));
        cmd.Parameters.AddWithValue("$fetched_at_utc", Db(payload.FetchedAtUtc));

        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InsertSiteRoleAsync(
        SqliteConnection conn,
        SqliteTransaction tx,
        PmWoDetailEnvelope payload,
        string roleType,
        JsonElement? site,
        CancellationToken cancellationToken)
    {
        if (site is null || site.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return;

        const string sql = """
        INSERT INTO pm_wo_people_departments (
            wo_no, role_type, site_no, site_code, site_name, fetched_at_utc
        )
        VALUES (
            $wo_no, $role_type, $site_no, $site_code, $site_name, $fetched_at_utc
        );
        """;

        await using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;

        cmd.Parameters.AddWithValue("$wo_no", payload.WoNo);
        cmd.Parameters.AddWithValue("$role_type", roleType);
        cmd.Parameters.AddWithValue("$site_no", Db(GetInt(site.Value, "siteNo")));
        cmd.Parameters.AddWithValue("$site_code", Db(GetString(site.Value, "siteCode")));
        cmd.Parameters.AddWithValue("$site_name", Db(GetString(site.Value, "siteName")));
        cmd.Parameters.AddWithValue("$fetched_at_utc", Db(payload.FetchedAtUtc));

        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    private static JsonElement? GetObject(JsonElement? parent, string name)
    {
        if (parent is null)
            return null;

        var p0 = parent.Value;

        if (p0.ValueKind != JsonValueKind.Object)
            return null;

        if (!p0.TryGetProperty(name, out var p))
            return null;

        if (p.ValueKind == JsonValueKind.Null || p.ValueKind == JsonValueKind.Undefined)
            return null;

        return p;
    }

    private static JsonElement? GetFirstArrayItem(JsonElement parent, string name)
    {
        if (parent.ValueKind != JsonValueKind.Object)
            return null;
        if (!parent.TryGetProperty(name, out var arr))
            return null;
        if (arr.ValueKind != JsonValueKind.Array || arr.GetArrayLength() == 0)
            return null;
        return arr.EnumerateArray().First();
    }

    private static string? GetString(JsonElement? parent, string name)
    {
        if (parent is null)
            return null;

        var p0 = parent.Value;

        if (p0.ValueKind != JsonValueKind.Object || !p0.TryGetProperty(name, out var p))
            return null;

        if (p.ValueKind == JsonValueKind.Null || p.ValueKind == JsonValueKind.Undefined)
            return null;

        return p.ValueKind == JsonValueKind.String ? p.GetString() : p.ToString();
    }

    private static int? GetInt(JsonElement? parent, string name)
    {
        if (parent is null)
            return null;

        var p0 = parent.Value;

        if (p0.ValueKind != JsonValueKind.Object || !p0.TryGetProperty(name, out var p))
            return null;

        if (p.ValueKind == JsonValueKind.Null || p.ValueKind == JsonValueKind.Undefined)
            return null;

        if (p.TryGetInt32(out var i))
            return i;

        if (int.TryParse(p.ToString(), out i))
            return i;

        return null;
    }

    private static decimal? GetDecimal(JsonElement? parent, string name)
    {
        if (parent is null)
            return null;

        var p0 = parent.Value;

        if (p0.ValueKind != JsonValueKind.Object || !p0.TryGetProperty(name, out var p))
            return null;

        if (p.ValueKind == JsonValueKind.Null || p.ValueKind == JsonValueKind.Undefined)
            return null;

        if (p.TryGetDecimal(out var d))
            return d;

        if (decimal.TryParse(p.ToString(), out d))
            return d;

        return null;
    }

    private static int? GetBoolInt(JsonElement? parent, string name)
    {
        if (parent is null)
            return null;

        var p0 = parent.Value;

        if (p0.ValueKind != JsonValueKind.Object || !p0.TryGetProperty(name, out var p))
            return null;

        if (p.ValueKind == JsonValueKind.Null || p.ValueKind == JsonValueKind.Undefined)
            return null;

        if (p.ValueKind == JsonValueKind.True) return 1;
        if (p.ValueKind == JsonValueKind.False) return 0;
        if (int.TryParse(p.ToString(), out var i)) return i;

        return null;
    }

    private static string? GetStringArrayCsv(JsonElement? parent, string name)
    {
        if (parent is null)
            return null;

        var p0 = parent.Value;

        if (p0.ValueKind != JsonValueKind.Object || !p0.TryGetProperty(name, out var p))
            return null;

        if (p.ValueKind != JsonValueKind.Array)
            return null;

        return string.Join(",", p.EnumerateArray().Select(x => x.ToString()));
    }

    private static string? GetIntArrayCsv(JsonElement? parent, string name)
    {
        if (parent is null)
            return null;

        var p0 = parent.Value;

        if (p0.ValueKind != JsonValueKind.Object || !p0.TryGetProperty(name, out var p))
            return null;

        if (p.ValueKind != JsonValueKind.Array)
            return null;

        return string.Join(",", p.EnumerateArray().Select(x => x.ToString()));
    }

    private static object Db(object? value) => value ?? DBNull.Value;
}