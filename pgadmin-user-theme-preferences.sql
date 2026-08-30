-- pgAdmin / PostgreSQL migration for missing theme preference table
-- Run this in the pgAdmin Query Tool.

CREATE TABLE IF NOT EXISTS public.user_theme_preferences (
    user_id character varying(450) NOT NULL,
    theme character varying(20) NOT NULL DEFAULT 'light',
    updated_at_utc timestamp without time zone NOT NULL DEFAULT NOW(),
    CONSTRAINT PK_user_theme_preferences PRIMARY KEY (user_id)
);

INSERT INTO public."__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260830000000_AddUserThemePreferences', '8.0.21')
ON CONFLICT ("MigrationId") DO NOTHING;
