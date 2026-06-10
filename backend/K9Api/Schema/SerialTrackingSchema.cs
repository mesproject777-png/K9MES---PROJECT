using Npgsql;

public static class SerialTrackingSchema
{
    public static async Task EnsureSerialTrackingSchemaAsync(NpgsqlConnection connection)
    {
        await SqlQuery.ExecuteAsync(connection, "CREATE SEQUENCE IF NOT EXISTS serial_rsn_seq START WITH 1");
        await SqlQuery.ExecuteAsync(
            connection,
            """
            CREATE TABLE IF NOT EXISTS serial_numbers (
              id BIGSERIAL PRIMARY KEY,
              sn VARCHAR(220) NOT NULL,
              rsn VARCHAR(40) NOT NULL UNIQUE DEFAULT ('RSN' || LPAD(nextval('serial_rsn_seq')::text, 10, '0')),
              work_order_id INTEGER NOT NULL REFERENCES work_orders(id) ON DELETE CASCADE,
              item_id INTEGER NOT NULL REFERENCES items(id) ON DELETE CASCADE,
              item_revision_id INTEGER REFERENCES item_revisions(id) ON DELETE SET NULL,
              site_id INTEGER REFERENCES sites(id) ON DELETE SET NULL,
              status VARCHAR(30) NOT NULL DEFAULT 'New',
              condition VARCHAR(30) NOT NULL DEFAULT 'Good',
              current_station_code VARCHAR(80),
              current_station_order INTEGER,
              last_moved_at TIMESTAMP,
              created_at TIMESTAMP NOT NULL DEFAULT NOW(),
              updated_at TIMESTAMP NOT NULL DEFAULT NOW()
            )
            """);
        await SqlQuery.ExecuteAsync(connection, "CREATE INDEX IF NOT EXISTS idx_serial_numbers_sn_upper ON serial_numbers (UPPER(sn))");
        await SqlQuery.ExecuteAsync(connection, "CREATE INDEX IF NOT EXISTS idx_serial_numbers_wo ON serial_numbers (work_order_id)");
        await SqlQuery.ExecuteAsync(connection, "CREATE INDEX IF NOT EXISTS idx_serial_numbers_item ON serial_numbers (item_id)");
        await SqlQuery.ExecuteAsync(connection, "ALTER TABLE serial_numbers ADD COLUMN IF NOT EXISTS scrap_previous_status VARCHAR(30)");
        await SqlQuery.ExecuteAsync(connection, "ALTER TABLE serial_numbers ADD COLUMN IF NOT EXISTS scrap_previous_condition VARCHAR(30)");
        await SqlQuery.ExecuteAsync(connection, "ALTER TABLE serial_numbers ADD COLUMN IF NOT EXISTS scrap_reason TEXT");
        await SqlQuery.ExecuteAsync(connection, "ALTER TABLE serial_numbers ADD COLUMN IF NOT EXISTS scrapped_by VARCHAR(100)");
        await SqlQuery.ExecuteAsync(connection, "ALTER TABLE serial_numbers ADD COLUMN IF NOT EXISTS scrapped_at TIMESTAMP");
        await SqlQuery.ExecuteAsync(
            connection,
            """
            CREATE TABLE IF NOT EXISTS serial_station_logs (
              id BIGSERIAL PRIMARY KEY,
              serial_id BIGINT NOT NULL REFERENCES serial_numbers(id) ON DELETE CASCADE,
              item_id INTEGER NOT NULL REFERENCES items(id) ON DELETE CASCADE,
              work_order_id INTEGER NOT NULL REFERENCES work_orders(id) ON DELETE CASCADE,
              station_code VARCHAR(80) NOT NULL,
              station_name VARCHAR(220),
              action_result VARCHAR(10) NOT NULL,
              remark TEXT,
              changed_by VARCHAR(100) NOT NULL DEFAULT 'system',
              before_station_code VARCHAR(80),
              before_station_order INTEGER,
              after_station_code VARCHAR(80),
              after_station_order INTEGER,
              created_at TIMESTAMP NOT NULL DEFAULT NOW()
            )
            """);
        await SqlQuery.ExecuteAsync(
            connection,
            """
            ALTER TABLE serial_station_logs
            ADD COLUMN IF NOT EXISTS station_length VARCHAR(40),
            ADD COLUMN IF NOT EXISTS pc_name VARCHAR(160),
            ADD COLUMN IF NOT EXISTS additional_info TEXT
            """);
        await SqlQuery.ExecuteAsync(
            connection,
            """
            CREATE TABLE IF NOT EXISTS serial_assembly_links (
              id BIGSERIAL PRIMARY KEY,
              parent_serial_id BIGINT NOT NULL REFERENCES serial_numbers(id) ON DELETE CASCADE,
              child_serial_id BIGINT NOT NULL REFERENCES serial_numbers(id) ON DELETE RESTRICT,
              station_code VARCHAR(80) NOT NULL,
              created_by VARCHAR(100) NOT NULL DEFAULT 'system',
              created_at TIMESTAMP NOT NULL DEFAULT NOW(),
              CONSTRAINT uq_serial_assembly_child UNIQUE (child_serial_id),
              CONSTRAINT uq_serial_assembly_parent_child_station UNIQUE (parent_serial_id, child_serial_id, station_code)
            )
            """);
        await SqlQuery.ExecuteAsync(
            connection,
            """
            CREATE TABLE IF NOT EXISTS packing_packages (
              id BIGSERIAL PRIMARY KEY,
              package_no VARCHAR(60) NOT NULL,
              package_type VARCHAR(20) NOT NULL CHECK (package_type IN ('BOX', 'SHIPMENT')),
              status VARCHAR(20) NOT NULL DEFAULT 'OPEN' CHECK (status IN ('OPEN', 'CLOSED', 'SHIPPED')),
              remark TEXT,
              created_by VARCHAR(100) NOT NULL DEFAULT 'system',
              created_at TIMESTAMP NOT NULL DEFAULT NOW(),
              updated_at TIMESTAMP NOT NULL DEFAULT NOW(),
              closed_by VARCHAR(100),
              closed_at TIMESTAMP,
              shipped_by VARCHAR(100),
              shipped_at TIMESTAMP,
              CONSTRAINT uq_packing_package_no UNIQUE (package_no)
            )
            """);
        await SqlQuery.ExecuteAsync(
            connection,
            """
            CREATE TABLE IF NOT EXISTS packing_package_items (
              id BIGSERIAL PRIMARY KEY,
              package_id BIGINT NOT NULL REFERENCES packing_packages(id) ON DELETE CASCADE,
              serial_id BIGINT NOT NULL REFERENCES serial_numbers(id) ON DELETE RESTRICT,
              added_by VARCHAR(100) NOT NULL DEFAULT 'system',
              added_at TIMESTAMP NOT NULL DEFAULT NOW(),
              CONSTRAINT uq_packing_pkg_serial UNIQUE (package_id, serial_id),
              CONSTRAINT uq_packing_serial UNIQUE (serial_id)
            )
            """);
    }
}