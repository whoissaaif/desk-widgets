-- Desk Widgets community backend (Supabase / Postgres 15+).
-- Apply once in the Supabase SQL editor. Safe to re-run: it recreates policies, functions and
-- views, but never drops tables or data.
--
-- The one guarantee everything rests on (design/community-plan.md, "Threat model"):
-- the bytes compiled on a PC are exactly the bytes the owner read and approved.
--   * A version is one C# source text, stored here. No files, no Storage.
--   * Uploaders can only INSERT pending versions. Only approve() / reject() change a version.
--   * approve() recomputes sha256(source) in Postgres and refuses if it differs from the hash of
--     the text the admin page rendered.
--   * anon can read approved rows only, and nothing else.
--
-- Accounts: anyone can ask for access (request_access); the owner accepts a request on the admin
-- page, which sends an invite email (functions/invite). The invitee then picks a username, password,
-- bio and photo (docs/account.html) and from then on signs in with username and password
-- (functions/login).

-- ---------------------------------------------------------------- tables

create table if not exists public.profiles (
  id         uuid primary key references auth.users (id) on delete cascade,
  username   text not null unique check (username ~ '^[a-z0-9-]{2,24}$'),  -- immutable: no UPDATE grant on it
  created_at timestamptz not null default now()
);

-- Added after the first release, so they're columns of their own (create table above is a no-op then).
alter table public.profiles add column if not exists bio text not null default '';
alter table public.profiles add column if not exists avatar text;  -- base64 JPEG, about 192 px square
alter table public.profiles drop constraint if exists profiles_bio_check;
alter table public.profiles add constraint profiles_bio_check check (length(bio) <= 160);
alter table public.profiles drop constraint if exists profiles_avatar_check;
alter table public.profiles add constraint profiles_avatar_check
  check (avatar is null or (length(avatar) <= 60000 and avatar ~ '^[A-Za-z0-9+/=]+$'));

-- People asking to upload. Written only through request_access(); read and decided by the owner.
create table if not exists public.access_requests (
  id         bigint generated always as identity primary key,
  email      text not null unique check (length(email) <= 254 and email ~ '^[^@\s]+@[^@\s]+\.[^@\s]+$'),
  status     text not null default 'pending' check (status in ('pending', 'accepted', 'rejected')),
  created_at timestamptz not null default now(),
  decided_at timestamptz
);

create table if not exists public.admins (
  user_id uuid primary key references public.profiles (id) on delete cascade
);

create table if not exists public.widgets (
  id         text primary key check (id ~ '^u\.[a-z0-9-]+\.[a-z0-9-]+$' and length(id) <= 64),
  owner      uuid not null references public.profiles (id) on delete cascade,
  slug       text not null check (slug ~ '^[a-z0-9-]{1,36}$'),
  created_at timestamptz not null default now()
);

create table if not exists public.versions (
  widget_id   text not null references public.widgets (id) on delete cascade,
  version     int  not null default 0,               -- 1, 2, 3... set by the trigger below
  name        text not null check (length(name) between 1 and 40),
  category    text not null check (category in ('clocks', 'productivity', 'weather', 'system', 'media', 'developer', 'sports')),
  description text not null default '' check (length(description) <= 120),
  api_version int  not null default 1 check (api_version >= 1),
  source      text not null check (octet_length(source) <= 524288),
  preview     text check (preview is null or (length(preview) <= 140000 and preview ~ '^[A-Za-z0-9+/=]+$')),  -- base64 PNG, ~100 KB
  status      text not null default 'pending' check (status in ('pending', 'approved', 'rejected')),
  sha256      text check (sha256 is null or sha256 ~ '^[0-9a-f]{64}$'),
  reviewed_at timestamptz,
  created_at  timestamptz not null default now(),
  primary key (widget_id, version),
  check ((status = 'approved') = (sha256 is not null))
);

-- ---------------------------------------------------------------- source must read as it compiles
-- Characters that make code look different from what the C# compiler sees ("Trojan Source",
-- CVE-2021-42574): line breaks only the compiler honors (lone CR, U+0085, U+2028, U+2029),
-- invisible characters and bidi controls. Widget code never needs them, so a version containing
-- one is refused here, whatever route the upload takes. Same rule as Plugins.HiddenCharacter (app)
-- and hiddenCharacters (website).
create or replace function public.source_is_plain(s text) returns boolean
language sql immutable set search_path = public as $$
  select replace(s, E'\r\n', E'\n') !~ '[\x01-\x08\x0b-\x1f\x7f-\u009f\u200B-\u200F\u2028\u2029\u202A-\u202E\u2060-\u2069\uFEFF]';
$$;

alter table public.versions drop constraint if exists versions_source_plain;
alter table public.versions add constraint versions_source_plain check (public.source_is_plain(source));

-- ---------------------------------------------------------------- helpers

create or replace function public.is_admin() returns boolean
language sql stable security definer set search_path = public as $$
  select exists (select 1 from public.admins where user_id = auth.uid());
$$;

create or replace function public.my_username() returns text
language sql stable security definer set search_path = public as $$
  select username from public.profiles where id = auth.uid();
$$;

-- The widgets and versions policies need each other's rows; reading them through these
-- definer functions (which bypass RLS) avoids policy recursion.
create or replace function public.owns_widget(p_widget text) returns boolean
language sql stable security definer set search_path = public as $$
  select exists (select 1 from public.widgets where id = p_widget and owner = auth.uid());
$$;

create or replace function public.has_approved(p_widget text) returns boolean
language sql stable security definer set search_path = public as $$
  select exists (select 1 from public.versions where widget_id = p_widget and status = 'approved');
$$;

-- Versions are numbered by the server (1 + the highest so far), whatever the client sent.
create or replace function public.next_version() returns trigger
language plpgsql security definer set search_path = public as $$
begin
  perform pg_advisory_xact_lock(hashtext(new.widget_id));
  select coalesce(max(version), 0) + 1 into new.version from public.versions where widget_id = new.widget_id;
  return new;
end;
$$;

drop trigger if exists versions_number on public.versions;
create trigger versions_number before insert on public.versions
  for each row execute function public.next_version();

-- ---------------------------------------------------------------- row level security

alter table public.profiles enable row level security;
alter table public.admins   enable row level security;
alter table public.widgets  enable row level security;
alter table public.versions enable row level security;
alter table public.access_requests enable row level security;

drop policy if exists profiles_read on public.profiles;
create policy profiles_read on public.profiles for select to authenticated
  using (id = auth.uid() or public.is_admin());

-- Invited people create their own profile once (docs/account.html); signing up is off, so every
-- account was invited. Afterwards only the bio and photo can change (see grants).
drop policy if exists profiles_insert on public.profiles;
create policy profiles_insert on public.profiles for insert to authenticated
  with check (id = auth.uid());

drop policy if exists profiles_update on public.profiles;
create policy profiles_update on public.profiles for update to authenticated
  using (id = auth.uid()) with check (id = auth.uid());

drop policy if exists access_requests_read on public.access_requests;
create policy access_requests_read on public.access_requests for select to authenticated
  using (public.is_admin());

drop policy if exists admins_read on public.admins;
create policy admins_read on public.admins for select to authenticated
  using (user_id = auth.uid());

drop policy if exists widgets_read on public.widgets;
create policy widgets_read on public.widgets for select to anon, authenticated
  using (
    public.has_approved(id)
    or owner = auth.uid()
    or public.is_admin()
  );

-- The id must be exactly u.<your username>.<slug>, compared with =, never LIKE.
drop policy if exists widgets_insert on public.widgets;
create policy widgets_insert on public.widgets for insert to authenticated
  with check (
    owner = auth.uid()
    and public.my_username() is not null
    and id = 'u.' || public.my_username() || '.' || slug
  );

drop policy if exists versions_read on public.versions;
create policy versions_read on public.versions for select to anon, authenticated
  using (
    status = 'approved'
    or public.owns_widget(widget_id)
    or public.is_admin()
  );

drop policy if exists versions_insert on public.versions;
create policy versions_insert on public.versions for insert to authenticated
  with check (
    status = 'pending' and sha256 is null and reviewed_at is null
    and public.owns_widget(widget_id)
  );

-- ---------------------------------------------------------------- grants
-- Supabase grants everything on public tables to anon and authenticated by default. Take it back
-- and hand out only what the policies above are for. No UPDATE or DELETE for anyone.
-- service_role needs SELECT on tables the Edge Functions read (admins, access_requests);
-- bypassrls skips row policies but not table-level grants.

revoke all on public.profiles, public.admins, public.widgets, public.versions, public.access_requests from anon, authenticated;
grant select on public.widgets, public.versions to anon, authenticated;
grant select on public.profiles, public.admins, public.access_requests to authenticated;
grant select on public.admins, public.access_requests to service_role;
grant insert (id, username, bio, avatar) on public.profiles to authenticated;
grant update (bio, avatar) on public.profiles to authenticated;
grant insert (id, owner, slug) on public.widgets to authenticated;
grant insert (widget_id, name, category, description, api_version, source, preview) on public.versions to authenticated;

-- ---------------------------------------------------------------- public views (no account needed)
-- Views run with their owner's rights (they bypass RLS), so each one filters to public data itself.

-- Authors' public profile: name, bio and photo. Never their email.
create or replace view public.authors as
  select id, username, bio, avatar from public.profiles;

-- The newest approved version of every widget, without its source: the Explore list.
create or replace view public.explore as
  select distinct on (v.widget_id)
         v.widget_id as id, v.version, v.name, v.category, v.description, v.api_version,
         v.sha256, v.preview, v.reviewed_at, p.username as author
    from public.versions v
    join public.widgets w on w.id = v.widget_id
    join public.profiles p on p.id = w.owner
   where v.status = 'approved'
   order by v.widget_id, v.version desc;

revoke all on public.authors, public.explore from anon, authenticated;
grant select on public.authors, public.explore to anon, authenticated;

-- ---------------------------------------------------------------- review (admin only)

create or replace function public.approve(p_widget text, p_version int, p_sha256 text) returns void
language plpgsql security definer set search_path = public as $$
declare
  v_source text;
begin
  if not public.is_admin() then raise exception 'not allowed'; end if;
  select source into v_source from public.versions
   where widget_id = p_widget and version = p_version and status = 'pending'
   for update;
  if v_source is null then raise exception 'no pending version % of %', p_version, p_widget; end if;
  -- The hash of the text the admin page showed must be the hash of what's stored.
  if encode(sha256(convert_to(v_source, 'UTF8')), 'hex') <> lower(p_sha256) then
    raise exception 'the source changed since you loaded it; reload and review again';
  end if;
  update public.versions
     set status = 'approved', sha256 = lower(p_sha256), reviewed_at = now()
   where widget_id = p_widget and version = p_version;
end;
$$;

create or replace function public.reject(p_widget text, p_version int) returns void
language plpgsql security definer set search_path = public as $$
begin
  if not public.is_admin() then raise exception 'not allowed'; end if;
  update public.versions set status = 'rejected', reviewed_at = now()
   where widget_id = p_widget and version = p_version and status = 'pending';
  if not found then raise exception 'no pending version % of %', p_version, p_widget; end if;
end;
$$;

-- ---------------------------------------------------------------- access requests

-- Anyone can ask. The answer is the same whether the address is new, already asked or already has
-- an account, so it can't be used to find out who has one. A full queue stops taking requests.
create or replace function public.request_access(p_email text) returns void
language plpgsql security definer set search_path = public as $$
declare
  v_email text := lower(trim(p_email));
begin
  if v_email is null or length(v_email) > 254 or v_email !~ '^[^@\s]+@[^@\s]+\.[^@\s]+$' then
    raise exception 'That email address doesn''t look right.';
  end if;
  if (select count(*) from public.access_requests where status = 'pending') >= 200 then
    raise exception 'Too many requests are waiting right now. Try again in a few days.';
  end if;
  insert into public.access_requests (email) values (v_email) on conflict (email) do nothing;
end;
$$;

-- Accepting sends an email, which needs the service key: that's functions/invite. Rejecting doesn't.
create or replace function public.reject_request(p_id bigint) returns void
language plpgsql security definer set search_path = public as $$
begin
  if not public.is_admin() then raise exception 'not allowed'; end if;
  update public.access_requests set status = 'rejected', decided_at = now()
   where id = p_id and status = 'pending';
  if not found then raise exception 'no pending request %', p_id; end if;
end;
$$;

revoke all on function public.request_access(text), public.reject_request(bigint) from public, anon, authenticated;
grant execute on function public.request_access(text) to anon, authenticated;
grant execute on function public.reject_request(bigint) to authenticated;

revoke all on function public.approve(text, int, text), public.reject(text, int),
                       public.is_admin(), public.my_username(), public.owns_widget(text), public.has_approved(text),
                       public.next_version(), public.source_is_plain(text) from public, anon, authenticated;
grant execute on function public.approve(text, int, text), public.reject(text, int) to authenticated;
-- Policies run as the caller, so the roles that read need these helpers.
grant execute on function public.is_admin(), public.my_username(), public.owns_widget(text), public.has_approved(text),
  public.source_is_plain(text)
  to anon, authenticated;
