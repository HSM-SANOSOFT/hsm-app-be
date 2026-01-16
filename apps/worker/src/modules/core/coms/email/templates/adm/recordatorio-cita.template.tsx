import { RecordatorioCitaTemplateDto } from '@hsm-lib/definitions/dtos';
export function RecordatorioCitaTemplate(
  data: RecordatorioCitaTemplateDto,
): React.ReactNode {
  return (
    <>
      <p>
        Le saludamos del Hospital Santamaría, le recordamos al paciente:{' '}
        <i>{data.patientName}</i> su cita médica agendada:
        <br />
        <br />
        Fecha: <b>{data.appointmentDate}</b>
        <br />
        Hora: <b>{data.appointmentTime}</b>
        <br />
        Detalles: <b>{data.appointmentDetails}</b>
        <br />
        Especialidad Médica: <b>{data.medicalSpecialty}</b>
        <br />
        Tipo de Cita: <b>{data.appointmentType}</b>
        <br />
        Nota: <b>{data.appointmentNote}</b>
        <br />
        <br />
        Considere que la hora de su turno puede ajustarse según la
        disponibilidad, con el fin de optimizar su experiencia en el
        establecimiento. Le notificaremos de haber cualquier cambio.
        <br />
        <br />
        Atentamente
        <br />
        Servicio al Paciente
        <br />
        Celular 0981764904
      </p>
    </>
  );
}
