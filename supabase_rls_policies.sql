-- ============================================================================
-- SCRIPT DE ROW LEVEL SECURITY (RLS) PARA SUPABASE POSTGRESQL - VITALBRIDGE
-- ============================================================================
-- Este script activa la seguridad a nivel de fila (RLS) y establece las políticas
-- de acceso para proteger los datos médicos privados entre cuidadores y permitir
-- que los pacientes vinculados registren tomas y emitan alertas de emergencia SOS.
--
-- Tablas cubiertas:
--   1. paciente
--   2. tratamiento
--   3. horario
--   4. toma
--   5. alerta (y alerta_sos si existe en la base de datos)
-- ============================================================================

-- ----------------------------------------------------------------------------
-- PASO 0: FUNCIONES AUXILIARES DE RESOLUCIÓN DE IDENTIDAD
-- ----------------------------------------------------------------------------

-- Función para verificar si un paciente está válidamente vinculado
CREATE OR REPLACE FUNCTION public.fn_paciente_activo(p_id_paciente UUID)
RETURNS BOOLEAN
LANGUAGE sql
STABLE
SECURITY DEFINER
AS $$
  SELECT EXISTS (
    SELECT 1 FROM public.paciente
    WHERE id_paciente = p_id_paciente
      AND id_estado_paciente = 1
  );
$$;

-- ----------------------------------------------------------------------------
-- PASO 1: TABLA 'paciente'
-- ----------------------------------------------------------------------------
ALTER TABLE IF EXISTS public.paciente ENABLE ROW LEVEL SECURITY;

-- Eliminar políticas previas para evitar duplicidad
DROP POLICY IF EXISTS "Cuidador: Ver sus propios pacientes" ON public.paciente;
DROP POLICY IF EXISTS "Cuidador: Registrar nuevos pacientes" ON public.paciente;
DROP POLICY IF EXISTS "Cuidador: Actualizar sus pacientes" ON public.paciente;
DROP POLICY IF EXISTS "Cuidador: Eliminar sus pacientes" ON public.paciente;
DROP POLICY IF EXISTS "Paciente: Consultar su propio perfil" ON public.paciente;

-- Políticas para el Cuidador (auth.uid() coincide con id_usuario)
CREATE POLICY "Cuidador: Ver sus propios pacientes"
ON public.paciente
FOR SELECT
TO authenticated
USING (auth.uid() = id_usuario);

CREATE POLICY "Cuidador: Registrar nuevos pacientes"
ON public.paciente
FOR INSERT
TO authenticated
WITH CHECK (auth.uid() = id_usuario);

CREATE POLICY "Cuidador: Actualizar sus pacientes"
ON public.paciente
FOR UPDATE
TO authenticated
USING (auth.uid() = id_usuario)
WITH CHECK (auth.uid() = id_usuario);

CREATE POLICY "Cuidador: Eliminar sus pacientes"
ON public.paciente
FOR DELETE
TO authenticated
USING (auth.uid() = id_usuario);

-- Política para el Paciente vinculado por token (modo paciente)
CREATE POLICY "Paciente: Consultar su propio perfil"
ON public.paciente
FOR SELECT
TO anon, authenticated
USING (
  EXISTS (
    SELECT 1 FROM public.token_paciente tp
    WHERE tp.id_paciente = public.paciente.id_paciente
  )
);

-- ----------------------------------------------------------------------------
-- PASO 2: TABLA 'tratamiento'
-- ----------------------------------------------------------------------------
ALTER TABLE IF EXISTS public.tratamiento ENABLE ROW LEVEL SECURITY;

DROP POLICY IF EXISTS "Cuidador: Ver tratamientos de sus pacientes" ON public.tratamiento;
DROP POLICY IF EXISTS "Cuidador: Crear tratamientos" ON public.tratamiento;
DROP POLICY IF EXISTS "Cuidador: Actualizar tratamientos" ON public.tratamiento;
DROP POLICY IF EXISTS "Cuidador: Borrar o dar de baja tratamientos" ON public.tratamiento;
DROP POLICY IF EXISTS "Paciente: Consultar sus tratamientos activos" ON public.tratamiento;

-- Políticas para el Cuidador
CREATE POLICY "Cuidador: Ver tratamientos de sus pacientes"
ON public.tratamiento
FOR SELECT
TO authenticated
USING (
  auth.uid() = id_usuario
  OR EXISTS (
    SELECT 1 FROM public.paciente p
    WHERE p.id_paciente = public.tratamiento.id_paciente
      AND p.id_usuario = auth.uid()
  )
);

CREATE POLICY "Cuidador: Crear tratamientos"
ON public.tratamiento
FOR INSERT
TO authenticated
WITH CHECK (
  auth.uid() = id_usuario
  OR EXISTS (
    SELECT 1 FROM public.paciente p
    WHERE p.id_paciente = public.tratamiento.id_paciente
      AND p.id_usuario = auth.uid()
  )
);

CREATE POLICY "Cuidador: Actualizar tratamientos"
ON public.tratamiento
FOR UPDATE
TO authenticated
USING (
  auth.uid() = id_usuario
  OR EXISTS (
    SELECT 1 FROM public.paciente p
    WHERE p.id_paciente = public.tratamiento.id_paciente
      AND p.id_usuario = auth.uid()
  )
)
WITH CHECK (
  auth.uid() = id_usuario
  OR EXISTS (
    SELECT 1 FROM public.paciente p
    WHERE p.id_paciente = public.tratamiento.id_paciente
      AND p.id_usuario = auth.uid()
  )
);

CREATE POLICY "Cuidador: Borrar o dar de baja tratamientos"
ON public.tratamiento
FOR DELETE
TO authenticated
USING (
  auth.uid() = id_usuario
  OR EXISTS (
    SELECT 1 FROM public.paciente p
    WHERE p.id_paciente = public.tratamiento.id_paciente
      AND p.id_usuario = auth.uid()
  )
);

-- Política para el Paciente: ver sus tratamientos activos
CREATE POLICY "Paciente: Consultar sus tratamientos activos"
ON public.tratamiento
FOR SELECT
TO anon, authenticated
USING (
  public.fn_paciente_activo(id_paciente)
  AND id_estado_tratamiento = 1
);

-- ----------------------------------------------------------------------------
-- PASO 3: TABLA 'horario'
-- ----------------------------------------------------------------------------
ALTER TABLE IF EXISTS public.horario ENABLE ROW LEVEL SECURITY;

DROP POLICY IF EXISTS "Cuidador: Gestionar horarios de sus tratamientos" ON public.horario;
DROP POLICY IF EXISTS "Paciente: Consultar sus horarios de medicamentos" ON public.horario;

-- El cuidador tiene control completo de los horarios de sus pacientes
CREATE POLICY "Cuidador: Gestionar horarios de sus tratamientos"
ON public.horario
FOR ALL
TO authenticated
USING (
  auth.uid() = id_usuario
  OR EXISTS (
    SELECT 1 FROM public.paciente p
    WHERE p.id_paciente = public.horario.id_paciente
      AND p.id_usuario = auth.uid()
  )
)
WITH CHECK (
  auth.uid() = id_usuario
  OR EXISTS (
    SELECT 1 FROM public.paciente p
    WHERE p.id_paciente = public.horario.id_paciente
      AND p.id_usuario = auth.uid()
  )
);

-- El paciente puede consultar los horarios programados para tomar sus medicinas
CREATE POLICY "Paciente: Consultar sus horarios de medicamentos"
ON public.horario
FOR SELECT
TO anon, authenticated
USING (
  public.fn_paciente_activo(id_paciente)
);

-- ----------------------------------------------------------------------------
-- PASO 4: TABLA 'toma'
-- ----------------------------------------------------------------------------
ALTER TABLE IF EXISTS public.toma ENABLE ROW LEVEL SECURITY;

DROP POLICY IF EXISTS "Cuidador: Ver y gestionar tomas de sus pacientes" ON public.toma;
DROP POLICY IF EXISTS "Paciente: Registrar confirmación de toma" ON public.toma;
DROP POLICY IF EXISTS "Paciente: Ver tomas confirmadas" ON public.toma;

-- Cuidador: Consulta y gestiona tomas
CREATE POLICY "Cuidador: Ver y gestionar tomas de sus pacientes"
ON public.toma
FOR ALL
TO authenticated
USING (
  EXISTS (
    SELECT 1 FROM public.horario h
    JOIN public.paciente p ON p.id_paciente = h.id_paciente
    WHERE h.id_horario = public.toma.id_horario
      AND (h.id_usuario = auth.uid() OR p.id_usuario = auth.uid())
  )
)
WITH CHECK (
  EXISTS (
    SELECT 1 FROM public.horario h
    JOIN public.paciente p ON p.id_paciente = h.id_paciente
    WHERE h.id_horario = public.toma.id_horario
      AND (h.id_usuario = auth.uid() OR p.id_usuario = auth.uid())
  )
);

-- Paciente: Permite insertar y actualizar la confirmación de la toma
CREATE POLICY "Paciente: Registrar confirmación de toma"
ON public.toma
FOR INSERT
TO anon, authenticated
WITH CHECK (
  EXISTS (
    SELECT 1 FROM public.horario h
    WHERE h.id_horario = public.toma.id_horario
      AND public.fn_paciente_activo(h.id_paciente)
  )
);

CREATE POLICY "Paciente: Ver tomas confirmadas"
ON public.toma
FOR SELECT
TO anon, authenticated
USING (
  EXISTS (
    SELECT 1 FROM public.horario h
    WHERE h.id_horario = public.toma.id_horario
      AND public.fn_paciente_activo(h.id_paciente)
  )
);

-- ----------------------------------------------------------------------------
-- PASO 5: TABLA 'alerta' (y compatibilidad con 'alerta_sos')
-- ----------------------------------------------------------------------------
DO $$
BEGIN
  -- Si existe la tabla alerta
  IF EXISTS (SELECT 1 FROM information_schema.tables WHERE table_name = 'alerta') THEN
    ALTER TABLE public.alerta ENABLE ROW LEVEL SECURITY;

    DROP POLICY IF EXISTS "Cuidador: Consultar alertas de sus pacientes" ON public.alerta;
    DROP POLICY IF EXISTS "Cuidador: Resolver alertas de sus pacientes" ON public.alerta;
    DROP POLICY IF EXISTS "Paciente: Emitir alerta de emergencia SOS" ON public.alerta;
    DROP POLICY IF EXISTS "Paciente: Ver estado de alerta emitida" ON public.alerta;

    -- Cuidador: Consulta alertas
    CREATE POLICY "Cuidador: Consultar alertas de sus pacientes"
    ON public.alerta
    FOR SELECT
    TO authenticated
    USING (
      EXISTS (
        SELECT 1 FROM public.paciente p
        WHERE p.id_paciente = public.alerta.id_paciente
          AND p.id_usuario = auth.uid()
      )
    );

    -- Cuidador: Modificar/Resolver alertas
    CREATE POLICY "Cuidador: Resolver alertas de sus pacientes"
    ON public.alerta
    FOR UPDATE
    TO authenticated
    USING (
      EXISTS (
        SELECT 1 FROM public.paciente p
        WHERE p.id_paciente = public.alerta.id_paciente
          AND p.id_usuario = auth.uid()
      )
    )
    WITH CHECK (
      EXISTS (
        SELECT 1 FROM public.paciente p
        WHERE p.id_paciente = public.alerta.id_paciente
          AND p.id_usuario = auth.uid()
      )
    );

    -- Paciente: Emitir alerta SOS en cualquier momento
    CREATE POLICY "Paciente: Emitir alerta de emergencia SOS"
    ON public.alerta
    FOR INSERT
    TO anon, authenticated
    WITH CHECK (
      public.fn_paciente_activo(id_paciente)
    );

    -- Paciente: Consultar estado de su propia alerta
    CREATE POLICY "Paciente: Ver estado de alerta emitida"
    ON public.alerta
    FOR SELECT
    TO anon, authenticated
    USING (
      public.fn_paciente_activo(id_paciente)
    );
  END IF;

  -- Si existe como alerta_sos (por compatibilidad de nombres)
  IF EXISTS (SELECT 1 FROM information_schema.tables WHERE table_name = 'alerta_sos') THEN
    ALTER TABLE public.alerta_sos ENABLE ROW LEVEL SECURITY;

    DROP POLICY IF EXISTS "Cuidador: Consultar alertas de sus pacientes (sos)" ON public.alerta_sos;
    DROP POLICY IF EXISTS "Cuidador: Resolver alertas de sus pacientes (sos)" ON public.alerta_sos;
    DROP POLICY IF EXISTS "Paciente: Emitir alerta SOS (sos)" ON public.alerta_sos;

    CREATE POLICY "Cuidador: Consultar alertas de sus pacientes (sos)"
    ON public.alerta_sos
    FOR SELECT
    TO authenticated
    USING (
      EXISTS (
        SELECT 1 FROM public.paciente p
        WHERE p.id_paciente = public.alerta_sos.id_paciente
          AND p.id_usuario = auth.uid()
      )
    );

    CREATE POLICY "Cuidador: Resolver alertas de sus pacientes (sos)"
    ON public.alerta_sos
    FOR UPDATE
    TO authenticated
    USING (
      EXISTS (
        SELECT 1 FROM public.paciente p
        WHERE p.id_paciente = public.alerta_sos.id_paciente
          AND p.id_usuario = auth.uid()
      )
    );

    CREATE POLICY "Paciente: Emitir alerta SOS (sos)"
    ON public.alerta_sos
    FOR INSERT
    TO anon, authenticated
    WITH CHECK (
      public.fn_paciente_activo(id_paciente)
    );
  END IF;
END $$;
