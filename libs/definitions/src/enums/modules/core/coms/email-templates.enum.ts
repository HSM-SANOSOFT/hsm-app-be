export enum AdmEmailTemplate {
  AutoAdmision = 'adm/auto-admision',
  Bienvenida = 'adm/bienvenida',
  CitasTurno = 'adm/citas-turno',
  DocumentoDerivacionAprobado = 'adm/documento-derivacion-aprobado',
  DocumentoDerivacionPendiente = 'adm/documento-derivacion-pendiente',
  DocumentoDerivacionRechazado = 'adm/documento-derivacion-rechazado',
  Reagendamiento = 'adm/reagendamiento',
  RecordatorioCita = 'adm/recordatorio-cita',
}

export enum AuthEmailTemplate {
  PinInicioSesion = 'auth/pin-inicio-sesion',
  PinRestablecerContrasena = 'auth/pin-restablecer-contrasena',
  PinRegistro = 'auth/pin-registro',
  PinResultadoExamen = 'auth/pin-resultado-examen',
}

export enum FacEmailTemplate {
  FacturaGenerada = 'fac/factura-generada',
  FacturaLista = 'fac/factura-lista',
}

export enum HisEmailTemplate {
  CertificadoMedico = 'his/certificado-medico',
  ProgramacionQuirofano = 'his/programacion-quirofano',
  RecetaMedica = 'his/receta-medica',
  ResultadoExamenImagen = 'his/resultado-examen-imagen',
  ResultadoExamenLaboratorio = 'his/resultado-examen-laboratorio',
  ResultadoExamenPatologia = 'his/resultado-examen-patologia',
  SolicitudExamenImagen = 'his/solicitud-examen-imagen',
  SolicitudExamenLaboratorio = 'his/solicitud-examen-laboratorio',
  SolicitudExamenPatologia = 'his/solicitud-examen-patologia',
}

export enum MktEmailTemplate {
  CampanaPromocional = 'mkt/campana-promocional',
  CotizacionServicio = 'mkt/cotizacion-servicio',
}

export enum BaseEmailTemplate {
  Base = 'base/base-template',
}

export const EmailTemplate = {
  Base: BaseEmailTemplate,
  Adm: AdmEmailTemplate,
  Fac: FacEmailTemplate,
  His: HisEmailTemplate,
  Auth: AuthEmailTemplate,
  Mkt: MktEmailTemplate,
};
