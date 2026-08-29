-- Neon / PostgreSQL migration for missing theme preference table
-- Run this in Neon SQL Editor or any PostgreSQL client connected to Neon.

CREATE TABLE IF NOT EXISTS public.user_theme_preferences (
    user_id character varying(450) NOT NULL,
    theme character varying(20) NOT NULL DEFAULT 'light',
    updated_at_utc timestamp without time zone NOT NULL DEFAULT NOW(),
    CONSTRAINT PK_user_theme_preferences PRIMARY KEY (user_id)
);

-- Optional: if you also want to mark the EF migration as applied in the migration history table
-- INSERT INTO public."__EFMigrationsHistory" ("MigrationId", "ProductVersion")
-- VALUES ('20260830000000_AddUserThemePreferences', '8.0.21')
-- ON CONFLICT ("MigrationId") DO NOTHING;
