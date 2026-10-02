GRANT SELECT ON groups, members TO corvees_app;
GRANT UPDATE (name, version, updated_at) ON groups TO corvees_app;
GRANT UPDATE (display_name, version, updated_at) ON members TO corvees_app;
GRANT SELECT, INSERT, UPDATE, DELETE ON locations, projects, steps, project_dependencies, step_dependencies TO corvees_app;
