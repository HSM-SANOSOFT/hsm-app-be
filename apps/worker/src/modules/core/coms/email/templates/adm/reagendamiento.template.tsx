import { ReagendamientoTemplateDto } from '@hsm-lib/definitions/dtos';
export function ReagendamientoTemplate(
  data: ReagendamientoTemplateDto,
): React.ReactNode {
  return (
    <>
      <p>
        Estimad@ paciente: <i>{data.patientName}</i>
        <br />
        <br />
        Le saludamos del Hospital Santamaría, para notificarle que, debido a un
        impedimento médico, su cita medica del día <b>{data.scheduledDate}</b>{' '}
        fue reagendada para:
        <br />
        <br />
        Dia: <b>{data.rescheduledDate}</b>
        <br />
        Hora: <b>{data.appointmentTime}</b>
        <br />
        Turno: <b>{data.appointmentDetails}</b>
        <br />
        Especialidad: <b>{data.medicalSpecialty}</b>
        <br />
        Tipo: <b>{data.appointmentType}</b>
        <br />
        <br />
        Atentamente,
        <br />
        Servicio al Paciente
        <br />
        Celular 0981764904
      </p>
    </>
  );
}
